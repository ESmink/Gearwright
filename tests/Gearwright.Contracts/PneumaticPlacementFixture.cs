using System;
using System.Collections.Generic;
using System.Linq;
using Gearwright.Pneumatics;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using static Gearwright.Contracts.PumpRuntimeFixture;

namespace Gearwright.Contracts;

internal static class PneumaticPlacementFixture
{
    private sealed class Rig
    {
        internal readonly BlockPneumaticTransport Block = new() { Code = new("gearwright:pneumatic-sender"), BlockId = 1 };
        internal readonly EntityPlayer Entity = new();
        internal readonly ItemSlot Slot = new DummySlot();
        internal readonly Dictionary<BlockPos, BlockEntityPneumaticTransport> Hosts = new();
        internal readonly HashSet<BlockPos> Blocked = new(), Denied = new();
        internal readonly IPlayer Player;
        internal readonly IWorldAccessor World;
        internal readonly ICoreAPI Api;
        internal long Now;
        internal int Placements;
        internal Rig(string kind = "sender")
        {
            Block.Code = new("gearwright:pneumatic-" + kind);
            var manager = Stub.Create<IPlayerInventoryManager>((m, _) => m.Name == "get_ActiveHotbarSlot" ? Slot : null);
            var data = Stub.Create<IWorldPlayerData>((m, _) => m.Name == "get_CurrentGameMode" ? EnumGameMode.Survival : null);
            Player = Stub.Create<IPlayer>((m, _) => m.Name switch {
                "get_Entity" => Entity, "get_PlayerUID" => "placer", "get_InventoryManager" => manager, "get_WorldData" => data, _ => null });
            var claims = Stub.Create<ILandClaimAPI>((m, a) => m.Name == "TryAccess" ? !Denied.Contains((BlockPos)a![1]!) : null);
            var empty = new Block { Replaceable = 10000 };
            var solid = new Block { Replaceable = 0 };
            var accessor = Stub.Create<IBlockAccessor>((m, a) => m.Name switch {
                "GetBlockEntity" => Hosts.GetValueOrDefault((BlockPos)a![0]!),
                "GetBlock" => Blocked.Contains((BlockPos)a![0]!) ? solid : Hosts.GetValueOrDefault((BlockPos)a![0]!)?.Block ?? empty,
                "SetBlock" => Place((BlockPos)a![1]!), _ => null });
            World = Stub.Create<IWorldAccessor>((m, _) => m.Name switch {
                "get_BlockAccessor" => accessor, "get_Claims" => claims, "get_Side" => EnumAppSide.Server,
                "get_ElapsedMilliseconds" => Now, "PlayerByUid" => Player, "get_Api" => Api,
                "GetIntersectingEntities" => Array.Empty<Entity>(), _ => null });
            Api = Stub.Create<ICoreAPI>((m, _) => m.Name switch { "get_World" => World, "get_Side" => EnumAppSide.Server, _ => null });
            Entity.World = World; Entity.WatchedAttributes.SetString("playerUID", "placer");
            Entity.Pos.SetPos(0, 1, 0);
            Slot.Itemstack = new ItemStack(Block, 2);
        }
        private object? Place(BlockPos pos)
        {
            var host = new BlockEntityPneumaticTransport { Api = Api, Pos = pos.Copy() };
            Set(host, "Block", Block); Hosts[pos.Copy()] = host; Placements++; return null;
        }
        internal static BlockSelection Selection(int x = 1, int y = 1, int z = 1) =>
            new() { Position = new(x, y, z), Face = BlockFacing.UP, HitPosition = new(.5, 1, .5) };
        internal void Click(BlockSelection? selection = null)
        {
            var handling = EnumHandHandling.NotHandled;
            Block.OnHeldInteractStart(Slot, Entity, selection ?? Selection(), null!, true, ref handling);
        }
        internal void Release() => Block.OnHeldInteractStop(.1f, Slot, Entity, Selection(), null!);
    }

    internal static void Run(Action<bool, string> check)
    {
        foreach (var face in BlockFacing.ALLFACES)
            check(PneumaticPlacement.Aim(face.Normali.X, face.Normali.Y, face.Normali.Z) == face,
                "Look placement can select " + face.Code);
        foreach (var output in BlockFacing.ALLFACES)
        foreach (var inventory in BlockFacing.ALLFACES.Where(f => f.Axis != output.Axis))
        {
            var matrix = PneumaticPlacement.EndpointMatrix(output, inventory);
            bool Port(float[] point, BlockFacing face)
            {
                var p = Mat4f.MulWithVec4(matrix, point);
                return Math.Abs(p[0] - .5 - face.Normali.X * .5) < 1e-6 &&
                    Math.Abs(p[1] - .5 - face.Normali.Y * .5) < 1e-6 && Math.Abs(p[2] - .5 - face.Normali.Z * .5) < 1e-6;
            }
            check(Port(new[] { 1f, .5f, .5f, 1f }, output) && Port(new[] { .5f, 0f, .5f, 1f }, inventory),
                "Endpoint mesh maps output " + output.Code + " and inventory " + inventory.Code + " to their real ports");
        }
        var r = new Rig();
        r.Click(); var plan = r.Block.Pending(r.World, r.Player)!;
        check(plan != null && r.Placements == 0 && r.Slot.StackSize == 2, "First endpoint click locks the tube preview without placing or consuming a block");
        r.Click();
        check(r.Placements == 0, "Holding the placement button cannot confirm the second stage");
        r.Release();
        var repeatedHandling = EnumHandHandling.NotHandled;
        r.Block.OnHeldInteractStart(r.Slot, r.Entity, Rig.Selection(), null!, false, ref repeatedHandling);
        check(r.Placements == 0, "Engine auto-repeat cannot confirm a released or moved-away preview");
        r.Entity.Pos.Pitch = (float)(Math.PI / 2);
        var branch = PneumaticPlacement.Aim(r.Player, plan!.Output.Axis).Opposite;
        var target = Rig.Selection(); target.Position = plan.Selection.Position.AddCopy(branch.Opposite);
        r.Click(target);
        var placed = r.Hosts[plan.Selection.Position];
        check(r.Placements == 1 && r.Slot.StackSize == 1 && placed.State.Input == plan.Input && placed.State.Output == plan.Output &&
            placed.State.InventoryFace == branch && placed.State.Owner == "placer" && r.Block.Pending(r.World, r.Player) == null,
            "Second click uses aim despite a different hovered face, keeps the locked position and consumes exactly one block");

        foreach (string denial in new[] { "claimed", "occupied", "out-of-reach", "cancelled", "changed-slot", "expired" })
        {
            r = new Rig(); r.Click(); r.Release(); var pos = r.Block.Pending(r.World, r.Player)!.Selection.Position;
            if (denial == "claimed") r.Denied.Add(pos);
            if (denial == "occupied") r.Blocked.Add(pos);
            if (denial == "out-of-reach") r.Entity.Pos.SetPos(50, 1, 0);
            if (denial == "cancelled") r.Entity.Controls.CtrlKey = true;
            if (denial == "changed-slot") r.Block.OnHeldInteractCancel(.1f, r.Slot, r.Entity, Rig.Selection(), null!, EnumItemUseCancelReason.ChangeSlot);
            if (denial == "expired") r.Now = 61000;
            r.Click();
            check(r.Placements == 0 && r.Slot.StackSize == 2, "A " + denial + " second stage never places or consumes the reserved block");
        }
        r = new Rig();
        var look = r.Block.PlacementFaces(r.World, r.Player, Rig.Selection());
        r.Entity.Controls.ShiftKey = true;
        var reversed = r.Block.PlacementFaces(r.World, r.Player, Rig.Selection());
        check(look.Output == PneumaticPlacement.Aim(r.Player).Opposite && reversed.Output == PneumaticPlacement.Aim(r.Player) &&
            look.Output == reversed.Input && look.Input == reversed.Output,
            "Normal placement faces opposite the view and Shift placement faces along it");

        foreach (string kind in new[] { "sender", "receiver" })
        foreach (bool firstShift in new[] { false, true })
        foreach (bool secondShift in new[] { false, true })
        {
            var stageRig = new Rig(kind);
            stageRig.Entity.Controls.ShiftKey = firstShift;
            stageRig.Click(); stageRig.Release();
            var locked = stageRig.Block.Pending(stageRig.World, stageRig.Player)!;
            var lockedOutput = locked.Output;
            stageRig.Entity.Pos.Pitch = (float)(Math.PI / 2);
            stageRig.Entity.Controls.ShiftKey = secondShift;
            var aim = PneumaticPlacement.Aim(stageRig.Player, lockedOutput.Axis);
            var expected = secondShift ? aim : aim.Opposite;
            var preview = PneumaticPlacement.InventoryDirection(locked, stageRig.Player);
            stageRig.Click();
            var host = stageRig.Hosts.Values.Single();
            check(preview == expected && host.State.InventoryFace == expected && host.State.Output == lockedOutput,
                kind + " second-stage Shift=" + secondShift + " independently selects the previewed branch after first-stage Shift=" + firstShift);
        }

        r.Entity.Controls.ShiftKey = false; r.Click(); r.Release(); r.Click();
        var h = r.Hosts.Values.Single();
        r.Slot.Itemstack = new ItemStack(new Item { Tool = EnumTool.Wrench });
        var selected = Rig.Selection(); selected.Position = h.Pos;
        var outputs = new HashSet<BlockFacing>();
        for (int i = 0; i < 6; i++) { r.Block.OnBlockInteractStart(r.World, r.Player, selected); outputs.Add(h.State.Output); }
        check(outputs.Count == 6 && h.State.Input == h.State.Output.Opposite, "Wrench redirects endpoint output through all six directions");
        var inventories = new HashSet<BlockFacing>(); var priorOutput = h.State.Output;
        r.Entity.Controls.ShiftKey = true;
        for (int i = 0; i < 4; i++) { r.Block.OnBlockInteractStart(r.World, r.Player, selected); inventories.Add(h.State.InventoryFace); }
        check(inventories.Count == 4 && inventories.All(f => f.Axis != priorOutput.Axis) && h.State.Output == priorOutput,
            "Shift-wrench cycles all four inventory branches while retaining the tube direction");
        h.State.Outstanding = "in-flight-order"; var priorBranch = h.State.InventoryFace;
        r.Block.OnBlockInteractStart(r.World, r.Player, selected);
        check(h.State.InventoryFace == priorBranch, "Wrench cannot move an inventory branch underneath an in-flight order");
        var routerRig = new Rig("router");
        var router = new BlockEntityPneumaticTransport { Api = routerRig.Api, Pos = new(1, 1, 1) };
        Set(router, "Block", routerRig.Block); routerRig.Hosts[router.Pos] = router;
        routerRig.Entity.Controls.ShiftKey = true;
        check(routerRig.Block.OnBlockInteractStart(routerRig.World, routerRig.Player, Rig.Selection()) && routerRig.Placements == 0 && routerRig.Slot.StackSize == 2,
            "Sneak interaction with a router consumes a held placeable stack's click without placing or consuming it");
    }
}
