using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Gearwright.Mechanics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;
using static Gearwright.Contracts.PumpRuntimeFixture;

namespace Gearwright.Contracts;

internal static class BellowsRendererFixture
{
    public static void Run(Action<bool, string> check)
    {
        string standard = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sdk", "standard.fsh"));
        foreach (BlockFacing nozzle in BlockFacing.HORIZONTALS)
        foreach (bool crankFirst in new[] { false, true })
        foreach (BlockFacing mounting in new[] { BlockFacing.DOWN, BlockFacing.UP })
        {
            Dictionary<BlockPos, BlockEntity> entities = new();
            IWorldChunk chunk = Stub.Create<IWorldChunk>((_, _) => null);
            IBlockAccessor accessor = Stub.Create<IBlockAccessor>((method, args) => method.Name switch
            {
                "GetBlockEntity" => entities.GetValueOrDefault((BlockPos)args![0]!),
                "GetChunkAtBlockPos" => chunk,
                "GetBlock" => entities.GetValueOrDefault((BlockPos)args![0]!)?.Block ?? new Block(),
                _ => null
            });
            var player = Stub.Create<IClientPlayer>((method, _) => method.Name == "get_Entity" ? new EntityPlayer() : null);
            var world = Stub.Create<IClientWorldAccessor>((method, _) => method.Name switch
            {
                "get_BlockAccessor" => accessor, "get_Player" => player,
                "get_Logger" => Stub.Create<ILogger>((_, _) => null), _ => null
            });
            int draws = 0, stops = 0;
            var shader = Stub.Create<IStandardShaderProgram>((method, _) =>
            { if (method.Name == "Stop") stops++; return null; });
            var render = Stub.Create<IRenderAPI>((method, args) =>
            {
                if (method.Name == "PreparedStandardShader") return shader;
                if (method.Name is "get_CameraMatrixOriginf" or "get_CurrentProjectionMatrix") return Mat4f.Create();
                if (method.Name == "RenderMultiTextureMesh")
                {
                    string sampler = (string)args![1]!;
                    // The installed engine shader is the oracle, not a copy of
                    // our renderer's constant. This reproduces the reported failure.
                    if (!Regex.IsMatch(standard, @"uniform\s+sampler2D\s+" + Regex.Escape(sampler) + @"\s*;"))
                        throw new InvalidOperationException("Renderer requested a sampler absent from the SDK shader: " + sampler);
                    draws++;
                }
                return null;
            });
            var api = Stub.Create<ICoreClientAPI>((method, _) => method.Name switch
            { "get_World" => world, "get_Side" => EnumAppSide.Client, "get_Render" => render, _ => null });
            var owner = new BlockEntityMechPoweredBellows { Api = api, Pos = new BlockPos(0, 0, 0) };
            var block = new BlockAutomaticBellow { Code = new("gearwright:automatic-bellow-" + nozzle.Code),
                Variant = new(new Dictionary<string, string> { ["side"] = nozzle.Code }) };
            Set(owner, "Block", block);
            var bellows = new BEBehaviorLargeBellows(owner); Set(bellows, "Api", api); owner.Behaviors.Add(bellows);
            var crankEntity = new BlockEntityMechPoweredBellows { Api = api, Pos = bellows.RearPosition.AddCopy(mounting) };
            string rotation = bellows.ShaftFace.Axis == EnumAxis.X ? "we" : "ns";
            var crankBlock = new BlockLateralCrank { Code = new("gearwright:lateral-crank-" + rotation),
                Variant = new(new Dictionary<string, string> { ["rotation"] = rotation }) };
            Set(crankEntity, "Block", crankBlock);
            var drive = new BEBehaviorMPLateralCrank(crankEntity); crankEntity.Behaviors.Add(drive);
            Set(drive, "Api", api); Set(drive, "gearedRatio", 1f); Set(drive, "network", new MechanicalNetwork());
            using var renderer = new LargeBellowsRenderer(bellows, api);
            // Supply uploaded sections without requiring a GPU. Exercise the
            // actual render entry point, selection, posing, draw and shader calls.
            foreach (string field in new[] { "body", "bottom", "top" })
            {
                Type boneType = typeof(LargeBellowsRenderer).GetNestedType("Bone", BindingFlags.NonPublic)!;
                object bone = Activator.CreateInstance(boneType, true)!;
                Set(bone, "Uploaded", new MultiTextureMeshRef(Array.Empty<MeshRef>(), Array.Empty<int>()));
                var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(boneType))!;
                list.Add(bone); Set(renderer, field, list);
            }
            if (crankFirst) entities[crankEntity.Pos] = crankEntity;
            entities[owner.Pos] = owner;
            if (!crankFirst)
            {
                renderer.OnRenderFrame(.016f, EnumRenderStage.Opaque);
                check(draws == 0, "Unconnected Automatic Bellow waits for its crank without drawing an attachment");
                entities[crankEntity.Pos] = crankEntity;
            }
            renderer.OnRenderFrame(.016f, EnumRenderStage.Opaque);
            drive.Network!.AngleRad = 1.7f;
            renderer.OnRenderFrame(.016f, EnumRenderStage.Opaque);
            check(draws == 4 && stops == 2 && bellows.DriveFace == mounting,
                $"Automatic Bellow renders with the installed standard shader: {nozzle.Code}, {mounting.Code}, crank first={crankFirst}");
            foreach (BlockFacing side in BlockFacing.HORIZONTALS)
                check(block.OrientedCode(side).ToString() == "gearwright:automatic-bellow-" + side.Code,
                    "Automatic Bellow placement retains its full dashed item ID");
            check(block.GetRotatedBlockCode(360).Equals(block.Code) &&
                block.GetRotatedBlockCode(90).Path.StartsWith("automatic-bellow-", StringComparison.Ordinal),
                "Automatic Bellow world rotation preserves the new item identity");
        }
    }
}
