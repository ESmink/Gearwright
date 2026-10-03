using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Gearwright.Hydraulics;
using Gearwright.Pneumatics;
using Gearwright.Rendering;
using Newtonsoft.Json;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using static Gearwright.Contracts.PumpRuntimeFixture;

namespace Gearwright.Contracts;

internal static class InspectionGlassFixture
{
    internal static void Run(Action<bool, string> check)
    {
        TerrainPasses(check);
        CreativeSourceTessellation(check);
        PneumaticStages(check);
    }

    private static IEnumerable<ShapeElement> Elements(Shape shape)
    {
        IEnumerable<ShapeElement> Visit(ShapeElement element)
        {
            yield return element;
            foreach (var child in element.Children ?? Array.Empty<ShapeElement>())
                foreach (var descendant in Visit(child)) yield return descendant;
        }
        return shape.Elements.SelectMany(Visit);
    }

    private static void TerrainPasses(Action<bool, string> check)
    {
        foreach (string file in new[] { "fluid-pipe-window.json", "creative-fluid-pump.json", "passive-fluid-pump.json",
                     "reciprocating-pump-body.json", "reciprocating-pump-body-supported.json" })
        {
            Shape source = JsonConvert.DeserializeObject<Shape>(File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "runtime-shapes", file)))!;
            short[] original = Elements(source).Select(e => e.RenderPass).ToArray();
            Shape prepared = InspectionGlass.TerrainShape(source);
            var before = Elements(source).ToArray();
            var after = Elements(prepared).ToArray();
            string glass = source.Textures.First(pair => pair.Value.Path == "block/inspection-glass").Key;
            var glassIndices = Enumerable.Range(0, before.Length).Where(index =>
                before[index].FacesResolved?.Any(face => face?.Enabled == true && face.Texture.TrimStart('#') == glass) == true).ToArray();
            check(glassIndices.Length > 0 && glassIndices.All(index =>
                after[index].RenderPass == (short)EnumChunkRenderPass.Transparent),
                file + " sends every glass pane to the SDK's native transparent pass");
            check(original.SequenceEqual(before.Select(e => e.RenderPass)) &&
                Enumerable.Range(0, before.Length).Except(glassIndices).All(index => after[index].RenderPass == original[index]),
                file + " keeps the shared shape and all opaque material passes unchanged");
            check(before.Length == after.Length && before.Zip(after).All(pair =>
                pair.First.Name == pair.Second.Name && pair.First.From.SequenceEqual(pair.Second.From) &&
                pair.First.To.SequenceEqual(pair.Second.To)),
                file + " preserves every approved part and its bounds");
        }
        var mesh = new MeshData(4, 6) { VerticesCount = 4 };
        InspectionGlass.PrepareAnimatedMesh(mesh);
        check(mesh.CustomFloats.Count == 4 && mesh.CustomFloats.Values.All(value => value == 0) &&
            mesh.CustomInts.Count == 4 && mesh.CustomInts.Values.All(value => value == 0) &&
            mesh.CustomInts.Conversion == DataConversion.Integer,
            "Native OIT vertices use undamaged identity-joint glass without a second animation transform");
    }

    private static void CreativeSourceTessellation(Action<bool, string> check)
    {
        Shape source = JsonConvert.DeserializeObject<Shape>(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "runtime-shapes", "creative-fluid-pump.json")))!;
        var asset = Stub.Create<IAsset>((method, _) => method.Name == "ToObject" ? source : null);
        var assets = Stub.Create<IAssetManager>((method, _) => method.Name == "TryGet" ? asset : null);
        var api = Stub.Create<ICoreAPI>((method, _) => method.Name == "get_Assets" ? assets : null);
        Shape? tessellated = null;
        MeshData mesh = new(4, 6);
        var tesselator = Stub.Create<ITesselatorAPI>((method, args) =>
        {
            if (method.Name == "TesselateShape")
            {
                tessellated = (Shape)args![1]!;
                args[2] = mesh;
            }
            return null;
        });
        MeshData? submitted = null;
        var mesher = Stub.Create<ITerrainMeshPool>((method, args) =>
        {
            if (method.Name == "AddMeshData") submitted = (MeshData)args![0]!;
            return null;
        });
        var entity = new BlockEntityCreativeFluidPump { Api = api, Pos = new(0, 0, 0) };
        Set(entity, "Block", new BlockCreativeFluidPump { Code = new("gearwright:creative-fluid-pump") });
        check(entity.OnTesselation(mesher, tesselator) && ReferenceEquals(submitted, mesh) &&
            tessellated != null && Elements(tessellated).Single(element => element.Name == "dial-face").RenderPass ==
                (short)EnumChunkRenderPass.Transparent &&
            Elements(source).Single(element => element.Name == "dial-face").RenderPass == 1,
            "The Creative Pipe Source submits transparent dial glass without changing the shared asset");
    }

    private static void PneumaticStages(Action<bool, string> check)
    {
        string vertex = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sdk", "entityanimated.vsh"));
        string fragment = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sdk", "entityanimated.fsh"));
        check(Regex.IsMatch(vertex, @"layout\(location = 4\) in float damageEffectIn") &&
            Regex.IsMatch(vertex, @"layout\(location = 5\) in int jointId") &&
            fragment.Contains("#if USEOIT==0") && fragment.Contains("OIT("),
            "The installed native OIT shader uses the supplied attributes and bypasses opaque SSAO outputs");
        foreach (bool failDraw in new[] { false, true })
        {
            var ubo = new IdentityBuffer();
            var buffers = new Vintagestory.API.Datastructures.OrderedDictionary<string, UBORef> { ["Animation"] = ubo };
            var uniforms = new Dictionary<string, object?>();
            var oitShader = Stub.Create<IShaderProgram>((method, args) =>
            {
                if (method.Name == "get_UBOs") return buffers;
                if (method.Name == "Uniform")
                {
                    string name = (string)args![0]!;
                    if (!Regex.IsMatch(vertex + fragment, @"uniform\s+\w+\s+" + Regex.Escape(name) + @"\b"))
                        throw new InvalidOperationException("Glass requested a uniform absent from the installed SDK shader: " + name);
                    uniforms[name] = args[1];
                }
                return null;
            });
            var standard = Stub.Create<IStandardShaderProgram>((_, _) => null);
            bool depthWrite = true, cull = true;
            int opaqueDraws = 0, glassDraws = 0, blendChanges = 0;
            var glassRef = new MultiTextureMeshRef(Array.Empty<MeshRef>(), Array.Empty<int>());
            var opaqueRef = new MultiTextureMeshRef(Array.Empty<MeshRef>(), Array.Empty<int>());
            var render = Stub.Create<IRenderAPI>((method, args) =>
            {
                if (method.Name == "PreparedStandardShader") return standard;
                if (method.Name == "GetEngineShader")
                {
                    check((EnumShaderProgram)args![0]! == EnumShaderProgram.Entityanimated_Oit,
                        "Pneumatic glass selects the SDK's native OIT shader");
                    return oitShader;
                }
                if (method.Name is "get_CameraMatrixOriginf" or "get_CurrentProjectionMatrix") return Mat4f.Create();
                if (method.Name == "get_AmbientColor") return new Vec3f(1, 1, 1);
                if (method.Name == "get_FogColor") return new Vec4f(1, 1, 1, 1);
                if (method.Name == "GLDepthMask") depthWrite = (bool)args![0]!;
                if (method.Name == "GlDisableCullFace") cull = false;
                if (method.Name == "GlEnableCullFace") cull = true;
                if (method.Name == "GlToggleBlend") blendChanges++;
                if (method.Name == "RenderMultiTextureMesh")
                {
                    if (ReferenceEquals(args![0], glassRef))
                    {
                        check(!depthWrite && !cull && (string)args[1]! == "entityTex",
                            "Glass uses the native sampler, both sides, and no depth writes");
                        glassDraws++;
                        if (failDraw) throw new InvalidOperationException("Fixture draw failure");
                    }
                    else opaqueDraws++;
                }
                return null;
            });
            var accessor = Stub.Create<IBlockAccessor>((method, _) => method.Name == "GetLightRGBs" ? new Vec4f(1, 1, 1, 1) : null);
            var player = Stub.Create<IClientPlayer>((method, _) => method.Name == "get_Entity" ? new EntityPlayer() : null);
            var world = Stub.Create<IClientWorldAccessor>((method, _) => method.Name switch
            { "get_BlockAccessor" => accessor, "get_Player" => player, _ => null });
            var events = Stub.Create<IClientEventAPI>((_, _) => null);
            var api = Stub.Create<ICoreClientAPI>((method, _) => method.Name switch
            { "get_World" => world, "get_Render" => render, "get_Event" => events, _ => null });
            var host = new BlockEntityPneumaticTransport { Api = api, Pos = new(0, 0, 0) };
            Set(host, "Block", new Block { Code = new("gearwright:pneumatic-tube") });
            host.State.Input = BlockFacing.WEST; host.State.Output = BlockFacing.EAST;
            using var renderer = new PneumaticRenderer(host, api);
            Type boneType = typeof(PneumaticRenderer).GetNestedType("Bone", BindingFlags.NonPublic)!;
            object bone = Activator.CreateInstance(boneType, true)!;
            Set(bone, "Uploaded", opaqueRef); Set(bone, "UploadedGlass", glassRef);
            var bones = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(boneType))!;
            bones.Add(bone); Set(renderer, "bones", bones); Set(renderer, "prepared", "straight");
            renderer.OnRenderFrame(.016f, EnumRenderStage.Opaque);
            check(opaqueDraws == 1 && glassDraws == 0 && depthWrite,
                "Opaque pneumatic rendering draws hardware without any glass in its SSAO buffers");
            int blendsBefore = blendChanges;
            depthWrite = false; // The engine enters OIT with depth writes disabled.
            try { renderer.OnRenderFrame(.016f, EnumRenderStage.OIT); }
            catch (InvalidOperationException exception) when (failDraw && exception.Message == "Fixture draw failure") { }
            check(glassDraws == 1 && opaqueDraws == 1 && blendChanges == blendsBefore &&
                uniforms["alphaTest"] is float threshold && threshold < 64f / 255 && ubo.Identity,
                "The OIT stage retains 25% glass and the engine's weighted blending without redrawing hardware");
            check(!depthWrite && cull, "Glass rendering preserves the OIT stage's depth state and restores culling after draws or failures");
        }
    }

    private sealed class IdentityBuffer : UBORef
    {
        internal bool Identity;
        public override void Bind() { }
        public override void Unbind() { }
        public override void Update<T>(T value) => Update(value!, 0, 64);
        public override void Update<T>(T value, int offset, int size) => Update((object)value!, offset, size);
        public override void Update(object value, int offset, int size) =>
            Identity = value is float[] matrix && matrix.SequenceEqual(Mat4f.Create()) && offset == 0 && size == 64;
    }
}
