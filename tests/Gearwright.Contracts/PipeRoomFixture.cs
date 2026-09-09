using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Gearwright.Hydraulics;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Stub = Gearwright.Contracts.PumpRuntimeFixture.Stub;

namespace Gearwright.Contracts;

/// <summary>Runs the installed room flood-fill and its real chunk event publisher.</summary>
internal static class PipeRoomFixture
{
    public static void Run(Action<bool, string> check)
    {
        foreach (BlockFacing pipeFace in BlockFacing.ALLFACES) CheckFace(pipeFace, check);
    }

    private static void CheckFace(BlockFacing pipeFace, Action<bool, string> check)
    {
        BlockPos center = new(16, 16, 16);
        BlockPos pipePos = center.AddCopy(pipeFace);
        BlockEntityFluidPipe pipe = new() { Pos = pipePos };
        BlockFluidPipe block = new() { BlockId = 4, Code = new("gearwright:fluid-pipe-copper") };
        Block air = new() { Code = new("game:air"), BlockMaterial = EnumBlockMaterial.Air };
        Block wall = new() { Code = new("game:planks-oak"), BlockMaterial = EnumBlockMaterial.Wood };
        foreach (Block model in new Block[] { air, wall, block }) model.BlockBehaviors = Array.Empty<BlockBehavior>();
        foreach (BlockFacing face in BlockFacing.ALLFACES)
        {
            wall.SideSolid[face.Index] = true;
            air.SideSolid[face.Index] = false;
            block.SideSolid[face.Index] = false;
        }
        Item plank = new() { ItemId = 1, Code = new("game:plank-oak") };
        List<(int Old, int New)> relights = new();
        int redraws = 0, roomEvents = 0;
        EnumAppSide side = EnumAppSide.Server;
        var chunk = Stub.Create<IWorldChunk>((method, _) => method.Name == "GetLocalBlockEntityAtBlockPos" ? pipe : null);
        Block GetBlock(BlockPos pos)
        {
            if (pos.Equals(pipe.Pos)) return block;
            int distance = Math.Max(Math.Max(Math.Abs(pos.X - center.X), Math.Abs(pos.Y - center.Y)), Math.Abs(pos.Z - center.Z));
            return distance == 1 ? wall : air;
        }
        var accessor = Stub.Create<ICachingBlockAccessor>((method, args) =>
        {
            if (method.Name == "MarkAbsorptionChanged") relights.Add(((int)args![0]!, (int)args[1]!));
            if (method.Name == "MarkBlockDirty") redraws++;
            return method.Name switch
            {
                "GetBlock" => args![0] is BlockPos pos ? GetBlock(pos) : air,
                "GetBlockEntity" => ((BlockPos)args![0]!).Equals(pipe.Pos) ? pipe : null,
                "GetChunkAtBlockPos" => chunk, "IsValidPos" => true, "get_LastChunkLoaded" => true,
                "get_MapSizeX" or "get_MapSizeY" or "get_MapSizeZ" => 256,
                "GetLightLevel" => 0, _ => null
            };
        });
        Type eventType = typeof(Vintagestory.Server.ServerMain).Assembly.GetType("Vintagestory.Server.ServerEventAPI", true)!;
        var events = (IEventAPI)Activator.CreateInstance(eventType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object?[] { null }, null)!;
        events.ChunkDirty += (_, _, _) => roomEvents++;
        var logger = Stub.Create<ILogger>((_, _) => null);
        var world = Stub.Create<IWorldAccessor>((method, _) => method.Name switch
        {
            "get_BlockAccessor" or "GetCachingBlockAccessor" => accessor,
            "get_SunBrightness" => 32, "GetItem" => plank, "get_Logger" => logger,
            "get_Side" => side, _ => null
        });
        var api = Stub.Create<ICoreAPI>((method, _) => method.Name switch
        {
            "get_World" => world, "get_Event" => events, "get_Side" => side, _ => null
        });
        pipe.Api = api;
        PumpRuntimeFixture.Set(pipe, "Block", block);
        PumpRuntimeFixture.Set(block, "api", api);
        PumpRuntimeFixture.Set(wall, "api", api);
        PumpRuntimeFixture.Set(air, "api", api);
        var playerData = Stub.Create<IWorldPlayerData>((method, _) => method.Name == "get_CurrentGameMode" ? EnumGameMode.Survival : null);
        var inventory = Stub.Create<IPlayerInventoryManager>((method, _) => method.Name == "TryGiveItemstack" ? true : null);
        var player = Stub.Create<IPlayer>((method, _) => method.Name switch
        {
            "get_WorldData" => playerData, "get_InventoryManager" => inventory, _ => null
        });
        RoomRegistry registry = new();
        registry.Start(api);
        PumpRuntimeFixture.Set(registry, "chunkMapSizeX", 8);
        PumpRuntimeFixture.Set(registry, "chunkMapSizeZ", 8);
        try
        {
            pipe.ConfigurePlacedPort(pipeFace);
            bool[] ports = new bool[6];
            ports[pipeFace.Index] = true;
            ports[pipeFace.Opposite.Index] = true;
            PumpRuntimeFixture.Set(pipe, "ports", ports);
            pipe.TryInstallWoodSupport(new(plank));
            Room open = registry.GetRoomForPosition(center);
            check(open.ExitCount > 0 && ReferenceEquals(open, registry.GetRoomForPosition(center)),
                $"The real room registry caches an open room through the uninsulated {pipeFace.Code} pipe");
            int beforeEvents = roomEvents;
            pipe.TryInstallWoodInsulation(new(plank));
            Room closed = registry.GetRoomForPosition(center);
            check(roomEvents == beforeEvents + 1 && !ReferenceEquals(open, closed) && closed.ExitCount == 0 &&
                  closed.Contains(center) && !closed.Contains(pipe.Pos) && closed.AnyChunkUnloaded == 0,
                $"Adding the second plank fires the engine event and seals the real room at its {pipeFace.Code} boundary");
            check(pipe.IsPortEnabled(pipeFace) && pipe.IsPortEnabled(pipeFace.Opposite) &&
                  block.GetLightAbsorption(accessor, pipe.Pos) == 32 && block.GetLightAbsorption(chunk, pipe.Pos) == 32,
                "The sealed pipe keeps both fluid ports and blocks skylight through both engine query paths");
            TreeAttribute insulated = new(); pipe.ToTreeAttributes(insulated);
            pipe.FromTreeAttributes(insulated, world);
            check(pipe.HasWoodInsulation && block.GetRetention(pipe.Pos, pipeFace, EnumRetentionType.Heat) == 1,
                "Reloading the real pipe keeps its room seal");
            pipe.TryRemoveWoodInsulation(player);
            Room reopened = registry.GetRoomForPosition(center);
            check(roomEvents == beforeEvents + 2 && reopened.ExitCount > 0 && !ReferenceEquals(closed, reopened) && pipe.HasWoodSupport,
                $"Removing the lining reopens the cached {pipeFace.Code} room while retaining the frame");
            check(relights.SequenceEqual(new[] { (0, 32), (32, 0) }), "Insulation transitions request the exact skylight changes");
            side = EnumAppSide.Client;
            int beforeRedraws = redraws;
            pipe.FromTreeAttributes(insulated, world);
            check(pipe.HasWoodInsulation && redraws == beforeRedraws + 1 && relights.Last() == (0, 32),
                "A received client state refreshes the insulated mesh and lighting");
        }
        finally { registry.Dispose(); }
    }
}
