using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Gearwright.Hydraulics;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Stub = Gearwright.Contracts.PumpRuntimeFixture.Stub;

namespace Gearwright.Contracts;

internal static class PipeSupportFixture
{
    public static void Run(Action<bool, string> check)
    {
        Dictionary<BlockPos, BlockEntity> entities = new();
        List<ItemStack> given = new(), dropped = new();
        bool room = true, access = true;
        EnumAppSide side = EnumAppSide.Server;
        EnumGameMode mode = EnumGameMode.Survival;
        Item plank = new() { ItemId = 1, Code = new("game:plank-oak") };
        Item sprinkler = new() { ItemId = 2, Code = new("gearwright:sprinkler-brass") };
        Block glass = new() { BlockId = 3, Code = new("game:glass-plain") };
        Block air = new() { Code = new("game:air"), BlockMaterial = EnumBlockMaterial.Air, Replaceable = 10000 };
        BlockFluidPipe block = new() { BlockId = 4, Code = new("gearwright:fluid-pipe-copper") };
        BlockBarrel barrel = new() { BlockId = 5, Code = new("game:barrel") };
        var accessor = Stub.Create<IBlockAccessor>((method, args) => method.Name switch
        {
            "GetBlockEntity" => entities.GetValueOrDefault((BlockPos)args![0]!),
            "GetBlock" => args![0] is BlockPos pos ? entities.ContainsKey(pos) ? block : air : glass,
            _ => null
        });
        var claims = Stub.Create<ILandClaimAPI>((method, _) => method.Name == "TryAccess" ? access : null);
        var logger = Stub.Create<ILogger>((_, _) => null);
        var world = Stub.Create<IWorldAccessor>((method, args) =>
        {
            if (method.Name == "SpawnItemEntity") dropped.Add(((ItemStack)args![0]!).Clone());
            return method.Name switch
            {
                "get_BlockAccessor" => accessor, "get_Logger" => logger, "get_Claims" => claims,
                "get_Side" => side, "GetBlock" => glass,
                "GetItem" => args![0] is int id ? id == 1 ? plank : sprinkler :
                    ((AssetLocation)args[0]!).Equals(plank.Code) ? plank : sprinkler,
                _ => null
            };
        });
        HydraulicNetworkSystem system = new();
        var loader = Stub.Create<IModLoader>((method, _) => method.Name == "GetModSystem" ? system : null);
        var api = Stub.Create<ICoreAPI>((method, _) => method.Name switch
        {
            "get_Side" => side, "get_World" => world, "get_ModLoader" => loader, _ => null
        });
        PumpRuntimeFixture.Set(block, "api", api);
        var playerData = Stub.Create<IWorldPlayerData>((method, _) => method.Name == "get_CurrentGameMode" ? mode : null);
        DummySlot slot = new(new ItemStack(plank, 8));
        var inventory = Stub.Create<IPlayerInventoryManager>((method, args) =>
        {
            if (method.Name == "get_ActiveHotbarSlot") return slot;
            if (method.Name != "TryGiveItemstack") return null;
            if (room) given.Add(((ItemStack)args![0]!).Clone());
            return room;
        });
        EntityPlayer entity = new();
        var player = Stub.Create<IPlayer>((method, _) => method.Name switch
        {
            "get_WorldData" => playerData, "get_InventoryManager" => inventory, "get_Entity" => entity, _ => null
        });
        BlockEntityFluidPipe NewPipe()
        {
            entities.Clear(); given.Clear(); dropped.Clear();
            var pipe = new BlockEntityFluidPipe { Api = api, Pos = new(0, 0, 0) };
            PumpRuntimeFixture.Set(pipe, "Block", block);
            entities[pipe.Pos] = pipe;
            return pipe;
        }
        static byte[] Bytes(ITreeAttribute tree)
        {
            using MemoryStream stream = new();
            using BinaryWriter writer = new(stream);
            tree.ToBytes(writer);
            return stream.ToArray();
        }
        foreach (int schema in new[] { 1, 3, 7, 8 })
        {
            var fixture = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", $"hydraulic-pipe-schema{schema}.json")));
            TreeAttribute state = new();
            foreach (var property in fixture.Properties())
            {
                if (property.Name == "woodSupportStack")
                {
                    ItemStack original = new(plank);
                    original.Attributes.SetString("owner-mark", "original-frame");
                    state.SetItemstack(property.Name, original);
                }
                else if (property.Name.StartsWith("addonStack-", StringComparison.Ordinal))
                {
                    ItemStack stack = property.Value.Value<string>("type") == "block" ? new(glass) : new(sprinkler);
                    state.SetItemstack(property.Name, stack);
                }
                else if (property.Name is "contentTemperatureC" or "contentAmountLitres") state.SetDouble(property.Name, property.Value.Value<double>());
                else if (property.Value.Type == JTokenType.Integer) state.SetInt(property.Name, property.Value.Value<int>());
                else if (property.Value.Type == JTokenType.Float) state.SetDouble(property.Name, property.Value.Value<double>());
                else if (property.Value.Type == JTokenType.String) state.SetString(property.Name, property.Value.Value<string>()!);
                else if (property.Name == "futureData")
                {
                    TreeAttribute future = new(); future.SetBool("keep", true); state["futureData"] = future;
                }
            }
            var pipe = NewPipe();
            TreeAttribute tree = new(); tree["gearwrightHydraulics"] = state;
            pipe.FromTreeAttributes(tree, world);
            TreeAttribute saved = new(); pipe.ToTreeAttributes(saved);
            pipe.FromTreeAttributes(saved, world);
            TreeAttribute again = new(); pipe.ToTreeAttributes(again);
            var stored = again.GetTreeAttribute("gearwrightHydraulics");
            check(pipe.CanWriteState && pipe.HasWoodSupport == (schema == 8) && !pipe.HasWoodInsulation &&
                  stored.GetInt("schemaVersion") == HydraulicStateSchema.CurrentVersion &&
                  stored.GetTreeAttribute("futureData").GetBool("keep") &&
                  Bytes(saved.GetTreeAttribute("gearwrightHydraulics")).SequenceEqual(Bytes(stored)),
                $"Real schema-{schema} pipe fixture preserves its support state and unknown fields through migration, save and reload");
            if (schema == 8) check(pipe.ContentAmountLitres == 6.5 && pipe.ContentTemperatureC == 28 &&
                stored.GetItemstack("woodSupportStack").Attributes.GetString("owner-mark") == "original-frame",
                "Schema 8 keeps its exact original frame plank and contents without adding insulation");
            if (schema == 7) check(pipe.ContentAmountLitres == 7.25 && pipe.ContentTemperatureC == 36 &&
                                  pipe.GetAddon(BlockFacing.EAST) == HydraulicFaceAddon.GlassWindow,
                "Schema 7 keeps exact fluid storage, temperature and its glass addon");
        }
        foreach (bool inventoryRoom in new[] { true, false })
        {
            room = inventoryRoom;
            var pipe = NewPipe();
            ItemStack input = new(plank, 8); input.Attributes.SetString("owner-mark", "retain");
            check(pipe.TryInstallWoodSupport(input) && !pipe.TryInstallWoodSupport(input), "Whole-pipe support installs only once");
            TreeAttribute tree = new(); pipe.ToTreeAttributes(tree); pipe.FromTreeAttributes(tree, world);
            check(pipe.HasWoodSupport && pipe.TryRemoveWoodSupport(player) && !pipe.TryRemoveWoodSupport(player) &&
                  (room ? given : dropped).Count == 1 && (room ? dropped : given).Count == 0 &&
                  (room ? given : dropped)[0].StackSize == 1 &&
                  (room ? given : dropped)[0].Attributes.GetString("owner-mark") == "retain",
                $"Saved wooden support returns its original plank once, with inventory room={room}");
            pipe.OnBlockBroken(player);
            check(dropped.Count == (room ? 0 : 1), "Breaking after support removal cannot duplicate the plank");
        }
        var supported = NewPipe();
        supported.TryInstallWoodSupport(new ItemStack(plank));
        check(block.SideIsSolid(accessor, supported.Pos, BlockFacing.UP.Index) &&
              block.SideIsSolid(supported.Pos, BlockFacing.UP.Index) &&
              block.CanAttachBlockAt(accessor, barrel, supported.Pos, BlockFacing.UP) &&
              !block.SideIsSolid(accessor, supported.Pos, BlockFacing.NORTH.Index),
            "Both engine solidity overloads and attachment queries see only the supported top as solid");
        supported.ConfigurePlacedPort(BlockFacing.UP);
        check(block.SideIsSolid(accessor, supported.Pos, BlockFacing.UP.Index), "An upward terminal pipe retains a solid barrel platform");
        var upper = new BlockEntityFluidPipe { Api = api, Pos = supported.Pos.UpCopy() };
        entities[upper.Pos] = upper;
        check(!block.SideIsSolid(accessor, supported.Pos, BlockFacing.UP.Index) &&
              !block.GetCollisionBoxes(accessor, supported.Pos).Any(box => box.X1 == 0 && box.X2 == 1 && box.Y2 == 1 && box.Z1 == 0 && box.Z2 == 1),
            "A pipe above removes the solid platform and its full collision slab immediately");
        entities.Remove(upper.Pos);
        BlockBehaviorUnstableFalling falling = new(barrel);
        falling.Initialize(JsonObject.FromJson("{}"));
        MethodInfo attached = typeof(BlockBehaviorUnstableFalling).GetMethod("IsAttached", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        check((bool)attached.Invoke(falling, new object[] { accessor, supported.Pos.UpCopy() })!,
            "Vanilla UnstableFalling recognizes a barrel resting on the supported terminal pipe");
        supported.OnBlockBroken(player);
        supported.OnBlockRemoved();
        check(dropped.Count == 1 && dropped[0].Collectible.Code.Equals(plank.Code), "Breaking and removing a supported pipe drops one plank");

        var protectedPipe = NewPipe(); protectedPipe.TryInstallWoodSupport(new ItemStack(plank));
        TreeAttribute validTree = new(); protectedPipe.ToTreeAttributes(validTree);
        foreach (int schema in new[] { 99, 8, 7 })
        {
            var saved = validTree.Clone(); var state = saved.GetTreeAttribute("gearwrightHydraulics");
            state.SetInt("schemaVersion", schema);
            if (schema != 99) state.SetInt("woodSupportStack", 123);
            byte[] original = Bytes(state);
            protectedPipe.FromTreeAttributes(saved, world);
            check(!protectedPipe.CanWriteState && !protectedPipe.TryRemoveWoodSupport(player) &&
                  !protectedPipe.TryInstallWoodSupport(new ItemStack(plank)), "Future or malformed support data rejects edits");
            TreeAttribute output = new(); protectedPipe.ToTreeAttributes(output);
            check(original.SequenceEqual(Bytes(output.GetTreeAttribute("gearwrightHydraulics"))),
                "Future or malformed support data retains its original bytes");
        }
        var interactive = NewPipe();
        BlockSelection selection = new() { Position = interactive.Pos, Face = BlockFacing.NORTH };
        access = false;
        block.OnBlockInteractStart(world, player, selection);
        check(!interactive.HasWoodSupport && slot.StackSize == 8, "Claim denial leaves the plank and pipe untouched");
        access = true; side = EnumAppSide.Client;
        block.OnBlockInteractStart(world, player, selection);
        check(!interactive.HasWoodSupport && slot.StackSize == 8, "Client preview cannot install or consume a support plank");
        side = EnumAppSide.Server;
        block.OnBlockInteractStart(world, player, selection);
        check(interactive.HasWoodSupport && slot.StackSize == 7, "The server interaction consumes exactly one plank for the support");
        access = false;
        block.OnBlockInteractStart(world, player, selection);
        check(!interactive.HasWoodInsulation && slot.StackSize == 7, "Claim denial also blocks the insulation upgrade without consuming a plank");
        access = true; side = EnumAppSide.Client;
        block.OnBlockInteractStart(world, player, selection);
        check(!interactive.HasWoodInsulation && slot.StackSize == 7, "Client preview cannot install insulation");
        side = EnumAppSide.Server;
        block.OnBlockInteractStart(world, player, selection);
        block.OnBlockInteractStart(world, player, selection);
        check(interactive.HasWoodInsulation && slot.StackSize == 6, "The second plank installs insulation and a third interaction consumes nothing");
        check(BlockFacing.ALLFACES.All(face => block.SideIsSolid(accessor, interactive.Pos, face.Index) &&
            block.SideIsSolid(interactive.Pos, face.Index) && block.CanAttachBlockAt(accessor, barrel, interactive.Pos, face) &&
            block.GetRetention(interactive.Pos, face, EnumRetentionType.Heat) == 1),
            "Every insulated face supports attachments and has wooden-wall heat retention");
        check(block.GetCollisionBoxes(accessor, interactive.Pos).Any(b => b.X1 == 0 && b.Y1 == 0 && b.Z1 == 0 && b.X2 == 1 && b.Y2 == 1 && b.Z2 == 1) &&
              block.GetSelectionBoxes(accessor, interactive.Pos).Any(b => b.X1 == 0 && b.Y1 == 0 && b.Z1 == 0 && b.X2 == 1 && b.Y2 == 1 && b.Z2 == 1),
            "Insulation has full-block collision and selection bounds");
        check(block.GetLightAbsorption(accessor, interactive.Pos) == 32, "Insulation blocks skylight through the casing");
        mode = EnumGameMode.Creative;
        dropped.Clear(); interactive.OnBlockBroken(player);
        check(dropped.Count == 0, "Creative pipe breaking does not manufacture either support plank");
        mode = EnumGameMode.Survival;
        foreach (bool inventoryRoom in new[] { true, false })
        {
            room = inventoryRoom;
            var pipe = NewPipe();
            ItemStack frame = new(plank, 5); frame.Attributes.SetString("owner-mark", "frame");
            ItemStack lining = new(plank, 3); lining.Attributes.SetString("owner-mark", "lining");
            check(!pipe.TryInstallWoodInsulation(lining) && pipe.TryInstallWoodSupport(frame) &&
                  pipe.TryInstallWoodInsulation(lining) && !pipe.TryRemoveWoodSupport(player),
                "Insulation requires a frame and cannot be orphaned by removing the frame first");
            lining.Attributes.SetString("owner-mark", "changed held plank");
            TreeAttribute tree = new(); pipe.ToTreeAttributes(tree); pipe.FromTreeAttributes(tree, world);
            check(pipe.HasWoodInsulation && pipe.TryRemoveWoodInsulation(player) && !pipe.TryRemoveWoodInsulation(player) &&
                  pipe.HasWoodSupport && !pipe.HasWoodInsulation && pipe.TryRemoveWoodSupport(player),
                $"Insulation and then frame remove independently after save/load with inventory room={room}");
            var returned = room ? given : dropped;
            check(returned.Count == 2 && returned.All(s => s.StackSize == 1) &&
                  returned[0].Attributes.GetString("owner-mark") == "lining" && returned[1].Attributes.GetString("owner-mark") == "frame",
                "Both original planks return in order with their saved attributes");
            pipe.OnBlockBroken(player);
            check((room ? dropped.Count == 0 : dropped.Count == 2), "Breaking the stripped pipe cannot duplicate either plank");
        }
        var broken = NewPipe();
        broken.TryInstallWoodSupport(new(plank)); broken.TryInstallWoodInsulation(new(plank));
        broken.TryInstallAddon(BlockFacing.NORTH, new(glass), HydraulicFaceAddon.GlassWindow, player);
        broken.OnBlockBroken(null!); broken.OnBlockRemoved();
        check(dropped.Count == 3 && dropped.Count(s => s.Item != null) == 2 && dropped.Count(s => s.Block != null) == 1,
            "Breaking an insulated fitted pipe returns both planks and its face fitting once");
        var malformed = NewPipe(); malformed.TryInstallWoodSupport(new(plank)); malformed.TryInstallWoodInsulation(new(plank));
        TreeAttribute insulatedTree = new(); malformed.ToTreeAttributes(insulatedTree);
        foreach (int issue in new[] { 0, 1, 2, 3, 4 })
        {
            var saved = insulatedTree.Clone(); var state = saved.GetTreeAttribute("gearwrightHydraulics");
            if (issue == 0) state.SetInt("woodInsulationStack", 123);
            if (issue == 1) state.GetItemstack("woodInsulationStack").StackSize = 2;
            if (issue == 2) state.RemoveAttribute("woodSupportStack");
            if (issue == 3) state.SetInt("schemaVersion", 99);
            if (issue == 4) { state.SetInt("schemaVersion", 8); state.SetInt("woodInsulationStack", 123); }
            byte[] original = Bytes(state);
            malformed.FromTreeAttributes(saved, world);
            check(!malformed.CanWriteState && !malformed.TryInstallWoodInsulation(new(plank)) &&
                  !malformed.TryRemoveWoodInsulation(player), "Malformed or newer insulation cannot be edited");
            TreeAttribute output = new(); malformed.ToTreeAttributes(output);
            check(original.SequenceEqual(Bytes(output.GetTreeAttribute("gearwrightHydraulics"))),
                "Malformed or newer insulation retains its exact original document");
        }
    }
}
