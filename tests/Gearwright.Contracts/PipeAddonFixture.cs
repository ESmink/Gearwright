using System;
using System.Collections.Generic;
using System.Linq;
using Gearwright.Hydraulics;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Stub = Gearwright.Contracts.PumpRuntimeFixture.Stub;

namespace Gearwright.Contracts;

/// <summary>Exercise actual pipe addon storage and return callbacks.</summary>
internal static class PipeAddonFixture
{
    public static void Run(Action<bool, string> check)
    {
        List<ItemStack> dropped = new(), given = new();
        bool inventoryHasRoom = true;
        EnumGameMode mode = EnumGameMode.Survival;
        Item item = new() { Code = new("gearwright:sprinkler-brass") };
        Block glass = new() { Code = new("game:glass-plain") };
        var accessor = Stub.Create<IBlockAccessor>((method, _) => method.Name == "GetBlock"
            ? new Block { BlockMaterial = EnumBlockMaterial.Air } : null);
        var world = Stub.Create<IWorldAccessor>((method, args) =>
        {
            if (method.Name == "SpawnItemEntity") dropped.Add(((ItemStack)args![0]!).Clone());
            return method.Name switch
            {
                "get_BlockAccessor" => accessor, "GetItem" => item, "GetBlock" => glass,
                "get_Logger" => Stub.Create<ILogger>((_, _) => null), _ => null
            };
        });
        HydraulicNetworkSystem system = new();
        var loader = Stub.Create<IModLoader>((method, _) => method.Name == "GetModSystem" ? system : null);
        var api = Stub.Create<ICoreAPI>((method, _) => method.Name switch
        {
            "get_Side" => EnumAppSide.Server, "get_World" => world, "get_ModLoader" => loader, _ => null
        });
        var data = Stub.Create<IWorldPlayerData>((method, _) => method.Name == "get_CurrentGameMode" ? mode : null);
        var inventory = Stub.Create<IPlayerInventoryManager>((method, args) =>
        {
            if (method.Name != "TryGiveItemstack") return null;
            if (inventoryHasRoom) given.Add(((ItemStack)args![0]!).Clone());
            return inventoryHasRoom;
        });
        var player = Stub.Create<IPlayer>((method, _) => method.Name switch
        {
            "get_WorldData" => data, "get_InventoryManager" => inventory, _ => null
        });
        BlockEntityFluidPipe NewPipe()
        {
            var pipe = new BlockEntityFluidPipe { Api = api, Pos = new(0, 0, 0) };
            PumpRuntimeFixture.Set(pipe, "Block", new BlockFluidPipe { Code = new("gearwright:fluid-pipe-copper") });
            return pipe;
        }
        void Reload(BlockEntityFluidPipe pipe)
        {
            TreeAttribute tree = new();
            pipe.ToTreeAttributes(tree);
            pipe.FromTreeAttributes(tree, world);
        }
        foreach (var addon in new[] { HydraulicFaceAddon.GlassWindow, HydraulicFaceAddon.Sprinkler,
                     HydraulicFaceAddon.PipeNozzle, HydraulicFaceAddon.CopperFlange })
        {
            BlockFacing face = addon == HydraulicFaceAddon.Sprinkler ? BlockFacing.DOWN : BlockFacing.NORTH;
            foreach (bool room in new[] { true, false })
            {
                inventoryHasRoom = room; dropped.Clear(); given.Clear();
                var pipe = NewPipe();
                if (addon == HydraulicFaceAddon.CopperFlange) pipe.ConfigurePlacedPort(face);
                ItemStack held = addon == HydraulicFaceAddon.GlassWindow ? new(glass, 8) : new(item, 8);
                held.Attributes.SetString("return-marker", addon.ToString());
                bool installed = pipe.TryInstallAddon(face, held, addon, player);
                held.Attributes.SetString("return-marker", "changed held stack");
                Reload(pipe);
                bool removed = pipe.TryRemoveAddon(face, player);
                var returned = room ? given : dropped;
                check(installed && removed && returned.Count == 1 && returned[0].StackSize == 1 &&
                      returned[0].Attributes.GetString("return-marker") == addon.ToString() &&
                      (room ? dropped : given).Count == 0 &&
                      !pipe.TryRemoveAddon(face, player),
                    $"{addon} returns one preserved addon after save/load with inventory room={room}, and cannot be removed twice");
                pipe.OnBlockBroken(player);
                check(dropped.Count == (room ? 0 : 1), $"Breaking the pipe after removing {addon} does not duplicate it");
            }
        }
        foreach (bool creative in new[] { false, true })
        {
            dropped.Clear(); given.Clear();
            mode = creative ? EnumGameMode.Creative : EnumGameMode.Survival;
            var pipe = NewPipe();
            foreach (BlockFacing face in BlockFacing.ALLFACES)
                pipe.TryInstallAddon(face, new ItemStack(glass), HydraulicFaceAddon.GlassWindow, player);
            Reload(pipe);
            pipe.OnBlockBroken(player);
            pipe.OnBlockRemoved();
            check(dropped.Count == (creative ? 0 : 6) && dropped.All(stack => stack.StackSize == 1),
                $"Breaking a six-addon pipe and removing its entity returns each addon once in {mode}");
        }
        mode = EnumGameMode.Survival;
        dropped.Clear();
        var explosion = NewPipe();
        explosion.TryInstallAddon(BlockFacing.DOWN, new ItemStack(item), HydraulicFaceAddon.Sprinkler, player);
        explosion.OnBlockBroken(null!);
        check(dropped.Count == 1, "Breaking a pipe without a player returns its stored addon");
        dropped.Clear();
        var unloaded = NewPipe();
        unloaded.TryInstallAddon(BlockFacing.DOWN, new ItemStack(item), HydraulicFaceAddon.Sprinkler, player);
        unloaded.OnBlockUnloaded();
        check(dropped.Count == 0, "Unloading a pipe never drops its stored addons");
    }
}
