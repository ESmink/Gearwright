using System;
using System.Linq;
using System.Collections.Generic;
using Gearwright.Hydraulics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Config;

namespace Gearwright.Pneumatics;

public sealed class BlockPneumaticTransport : Block
{
    private readonly Dictionary<string, PneumaticPlacementPlan> plans = new();
    private PneumaticPlacementPlan? confirming;
    internal bool IsEndpoint => Code.Path is "pneumatic-sender" or "pneumatic-receiver";
    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        if (Code.Path == "pneumatic-router") PlacedPriorityInteract = true;
    }

    internal PneumaticPlacementPlan? Pending(IWorldAccessor world, IPlayer player)
    {
        if (!plans.TryGetValue(player.PlayerUID, out var plan)) return null;
        if (plan.Expires < world.ElapsedMilliseconds || !ReferenceEquals(plan.Slot, player.InventoryManager.ActiveHotbarSlot) ||
            plan.Slot.Itemstack?.Block != this || !PneumaticPlacement.InReach(player, plan.Selection.Position))
        { plans.Remove(player.PlayerUID); return null; }
        return plan;
    }

    internal (BlockFacing Input, BlockFacing Output) PlacementFaces(IWorldAccessor world, IPlayer player, BlockSelection selection)
    {
        var output = Code.Path == "pneumatic-accumulator" ? SuggestedHVOrientation(player, selection)[0].Opposite : PneumaticPlacement.Aim(player);
        if (!player.Entity.Controls.ShiftKey) output = output.Opposite;
        var input = output.Opposite;
        var behind = world.BlockAccessor.GetBlockEntity(selection.Position.AddCopy(selection.Face.Opposite));
        BlockFacing? previous = behind is BlockEntityPneumaticTransport p ? p.State.Output : behind is BlockEntityPneumaticAirIntake a ? a.Outlet : null;
        if (previous == selection.Face)
        {
            input = selection.Face.Opposite;
            if (Code.Path != "pneumatic-tube" || output == input) output = input.Opposite;
        }
        return (input, output);
    }

    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection selection,
        EntitySelection entitySelection, bool firstEvent, ref EnumHandHandling handling)
    {
        if (!IsEndpoint || byEntity is not EntityPlayer entity)
        { base.OnHeldInteractStart(slot, byEntity, selection, entitySelection, firstEvent, ref handling); return; }
        handling = EnumHandHandling.PreventDefault;
        if (!firstEvent) return;
        var player = entity.Player;
        var world = byEntity.World;
        var plan = Pending(world, player);
        if (byEntity.Controls.CtrlKey)
        { plans.Remove(player.PlayerUID); return; }
        if (plan == null)
        {
            if (selection == null || slot.Empty) return;
            var target = PneumaticPlacement.Target(world, this, selection);
            string failure = "";
            if (!PneumaticPlacement.InReach(player, target.Position) ||
                !world.Claims.TryAccess(player, target.Position, EnumBlockAccessFlags.BuildOrBreak) ||
                !CanPlaceBlock(world, player, target, ref failure)) return;
            foreach (string uid in plans.Where(p => p.Value.Expires < world.ElapsedMilliseconds).Select(p => p.Key).ToArray()) plans.Remove(uid);
            if (plans.Count >= 256) return;
            var (input, output) = PlacementFaces(world, player, target);
            plans[player.PlayerUID] = new() { Selection = target, Slot = slot, Input = input, Output = output,
                Inventory = PneumaticPlacement.DefaultInventory(output), Expires = world.ElapsedMilliseconds + 60000 };
            return;
        }
        if (!plan.Released || slot.Empty) return;
        plan.Inventory = PneumaticPlacement.InventoryDirection(plan, player);
        string error = "";
        bool placed = false;
        try
        {
            confirming = plan;
            if (PneumaticPlacement.InReach(player, plan.Selection.Position) &&
                world.Claims.TryAccess(player, plan.Selection.Position, EnumBlockAccessFlags.BuildOrBreak))
                placed = base.TryPlaceBlock(world, player, slot.Itemstack!, plan.Selection, ref error);
        }
        finally { confirming = null; plans.Remove(player.PlayerUID); }
        if (!placed)
        {
            if (world.Api is ICoreClientAPI client) client.TriggerIngameError(this, "pneumatic-placement", Lang.Get("gearwright:pneumatic-place-failed"));
            return;
        }
        if (world.Side == EnumAppSide.Server && player.WorldData.CurrentGameMode != EnumGameMode.Creative)
        { slot.TakeOut(1); slot.MarkDirty(); }
        if (Sounds?.Place != null) world.PlaySoundAt(Sounds.Place, plan.Selection.Position, 0, player);
    }

    public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity,
        BlockSelection selection, EntitySelection entitySelection) => IsEndpoint || base.OnHeldInteractStep(secondsUsed, slot, byEntity, selection, entitySelection);

    public override void OnHeldInteractStop(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection selection, EntitySelection entitySelection)
    {
        if (IsEndpoint && byEntity is EntityPlayer player && plans.TryGetValue(player.PlayerUID, out var plan)) plan.Released = true;
        else base.OnHeldInteractStop(secondsUsed, slot, byEntity, selection, entitySelection);
    }

    public override bool OnHeldInteractCancel(float secondsUsed, ItemSlot slot, EntityAgent byEntity,
        BlockSelection selection, EntitySelection entitySelection, EnumItemUseCancelReason reason)
    {
        if (IsEndpoint && byEntity is EntityPlayer player && plans.TryGetValue(player.PlayerUID, out var plan))
        {
            if (reason == EnumItemUseCancelReason.ReleasedMouse || reason == EnumItemUseCancelReason.MovedAway) plan.Released = true;
            else plans.Remove(player.PlayerUID);
        }
        return true;
    }

    public override bool DoPlaceBlock(IWorldAccessor world, IPlayer player, BlockSelection selection, ItemStack stack)
    {
        if (!base.DoPlaceBlock(world, player, selection, stack)) return false;
        var pos = selection.Position;
        var (input, output) = confirming != null ? (confirming.Input, confirming.Output) : PlacementFaces(world, player, selection);
        if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityPneumaticAirIntake intake)
        { intake.SetOutlet(output); return true; }
        if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityPneumaticTransport host) return true;
        host.State.Owner = player.PlayerUID;
        host.State.Input = input; host.State.Output = output;
        host.State.InventoryFace = confirming?.Inventory ?? PneumaticPlacement.DefaultInventory(output);
        if (host.Kind == PneumaticLineKind.Router)
        {
            host.Router.Mount = selection.Face.Opposite;
            host.Router.Forward = PneumaticPlacement.Aim(player, host.Router.Mount.Axis);
        }
        host.MarkDirty(true);
        return true;
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer player, BlockSelection selection)
    {
        ItemStack? held = player.InventoryManager.ActiveHotbarSlot.Itemstack;
        bool wrench = held?.Collectible.Tool == EnumTool.Wrench;
        if (!wrench && (held == null || player.Entity.Controls.ShiftKey) &&
            world.BlockAccessor.GetBlockEntity(selection.Position) is BlockEntityPneumaticTransport router && router.Kind == PneumaticLineKind.Router)
        {
            if (PneumaticPlacement.InReach(player, selection.Position) && world.Claims.TryAccess(player, selection.Position, EnumBlockAccessFlags.Use) &&
                world.Side == EnumAppSide.Client) router.OpenRouterDialog(selection.SelectionBoxIndex > 0 ? selection.SelectionBoxIndex : 1);
            return true;
        }
        if (held != null && !wrench) return base.OnBlockInteractStart(world, player, selection);
        if (world.Side == EnumAppSide.Client)
        {
            if (!wrench && world.BlockAccessor.GetBlockEntity(selection.Position) is BlockEntityPneumaticTransport clientHost && clientHost.Kind == PneumaticLineKind.Router)
                clientHost.OpenRouterDialog(selection.SelectionBoxIndex > 0 ? selection.SelectionBoxIndex : 1);
            return true;
        }
        if (!world.Claims.TryAccess(player, selection.Position, EnumBlockAccessFlags.BuildOrBreak) ||
            !PneumaticPlacement.InReach(player, selection.Position)) return true;
        if (world.BlockAccessor.GetBlockEntity(selection.Position) is BlockEntityPneumaticAirIntake intake)
        {
            if (wrench && intake.CanWriteState)
            {
                var faces = BlockFacing.HORIZONTALS;
                intake.SetOutlet(faces[(Array.IndexOf(faces, intake.Outlet) + 1) % faces.Length]);
            }
            return true;
        }
        if (world.BlockAccessor.GetBlockEntity(selection.Position) is not BlockEntityPneumaticTransport host || !host.CanWrite) return true;
        if (wrench)
        {
            var s = host.State;
            if (host.Kind == PneumaticLineKind.Router)
            {
                if (s.Cargo != null || host.Router.ExpectedParcel != "") return true;
                if (player.Entity.Controls.ShiftKey)
                {
                    var faces = BlockFacing.ALLFACES;
                    host.Router.Mount = faces[(Array.IndexOf(faces, host.Router.Mount) + 1) % faces.Length];
                    if (host.Router.Forward.Axis == host.Router.Mount.Axis) host.Router.Forward = host.Router.Mount.IsHorizontal ? BlockFacing.UP : BlockFacing.EAST;
                }
                else
                {
                    var faces = BlockFacing.ALLFACES.Where(f => f.Axis != host.Router.Mount.Axis).ToArray();
                    host.Router.Forward = faces[(Array.IndexOf(faces, host.Router.Forward) + 1) % faces.Length];
                }
            }
            else if (host.Kind == PneumaticLineKind.Tube && player.Entity.Controls.ShiftKey) (s.Input, s.Output) = (s.Output, s.Input);
            else if (host.Kind == PneumaticLineKind.Tube)
            {
                var faces = BlockFacing.ALLFACES.Where(f => f != s.Input).ToArray();
                s.Output = faces[(Array.IndexOf(faces, s.Output) + 1) % faces.Length];
            }
            else
            {
                if (s.Cargo != null || s.Returning || s.Outstanding != "")
                {
                    (player as Vintagestory.API.Server.IServerPlayer)?.SendMessage(GlobalConstants.GeneralChatGroup,
                        Lang.Get("gearwright:pneumatic-rotate-busy"), EnumChatType.Notification);
                    return true;
                }
                if (player.Entity.Controls.ShiftKey)
                {
                    var faces = BlockFacing.ALLFACES.Where(f => f.Axis != s.Output.Axis).ToArray();
                    s.InventoryFace = faces[(Array.IndexOf(faces, s.InventoryFace) + 1) % faces.Length];
                }
                else
                {
                    var faces = BlockFacing.ALLFACES;
                    s.Output = faces[(Array.IndexOf(faces, s.Output) + 1) % faces.Length]; s.Input = s.Output.Opposite;
                    if (s.InventoryFace.Axis == s.Output.Axis) s.InventoryFace = PneumaticPlacement.DefaultInventory(s.Output);
                }
            }
            host.MarkDirty(true);
        }
        else if (host.Kind != PneumaticLineKind.Router)
        {
            var text = new System.Text.StringBuilder(); host.GetBlockInfo(player, text);
            (player as Vintagestory.API.Server.IServerPlayer)?.SendMessage(GlobalConstants.GeneralChatGroup, text.ToString(), EnumChatType.Notification);
        }
        return true;
    }

    public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer? player, float dropQuantityMultiplier = 1)
    {
        if (world.Side == EnumAppSide.Server && world.BlockAccessor.GetBlockEntity(pos) is BlockEntityPneumaticTransport host &&
            (!host.CanWrite || !world.Api.ModLoader.GetModSystem<PneumaticNetworkSystem>().Drop(host))) return;
        base.OnBlockBroken(world, pos, player, dropQuantityMultiplier);
    }

    public override void OnBlockExploded(IWorldAccessor world, BlockPos pos, BlockPos explosionCenter, EnumBlastType blastType, string ignitedByPlayerUid)
    {
        if (world.Side == EnumAppSide.Server && world.BlockAccessor.GetBlockEntity(pos) is BlockEntityPneumaticTransport host &&
            (!host.CanWrite || !world.Api.ModLoader.GetModSystem<PneumaticNetworkSystem>().Drop(host))) return;
        base.OnBlockExploded(world, pos, explosionCenter, blastType, ignitedByPlayerUid);
    }

    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot slot) => Code.Path == "pneumatic-router" ? new[] {
        new WorldInteraction { ActionLangCode = "gearwright:pneumatic-help-router-place", MouseButton = EnumMouseButton.Right },
    } : IsEndpoint ? new[] {
        new WorldInteraction { ActionLangCode = "gearwright:pneumatic-help-two-click", MouseButton = EnumMouseButton.Right },
        new WorldInteraction { ActionLangCode = "gearwright:pneumatic-help-cancel", MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl" },
    } : new[] {
        new WorldInteraction { ActionLangCode = "gearwright:pneumatic-help-place", MouseButton = EnumMouseButton.Right },
    };

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor accessor, BlockPos pos)
    {
        var h = accessor.GetBlockEntity(pos) as BlockEntityPneumaticTransport;
        if (h == null && (confirming == null || !confirming.Selection.Position.Equals(pos)))
            return base.GetSelectionBoxes(accessor, pos);
        if (h?.Kind == PneumaticLineKind.Tube) return new[] { Segment(h.State.Input), Segment(h.State.Output) };
        if (h?.Kind == PneumaticLineKind.Router) return new[] { TransformBox(new Cuboidf(.06f, 0, .06f, .94f, .2f, .94f), h.Router.Matrix) }
            .Concat(Enumerable.Range(1, 4).Select(n => Segment(h.Router.Face(n)))).ToArray();
        var matrix = PneumaticPlacement.EndpointMatrix(h?.State.Output ?? confirming!.Output, h?.State.InventoryFace ?? confirming!.Inventory);
        return base.GetSelectionBoxes(accessor, pos).Select(box =>
        {
            var a = Mat4f.MulWithVec4(matrix, new[] { box.X1, box.Y1, box.Z1, 1f });
            var b = Mat4f.MulWithVec4(matrix, new[] { box.X2, box.Y2, box.Z2, 1f });
            return new Cuboidf(Math.Min(a[0], b[0]), Math.Min(a[1], b[1]), Math.Min(a[2], b[2]),
                Math.Max(a[0], b[0]), Math.Max(a[1], b[1]), Math.Max(a[2], b[2]));
        }).ToArray();
    }
    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor accessor, BlockPos pos) => GetSelectionBoxes(accessor, pos);
    private static Cuboidf TransformBox(Cuboidf box, float[] matrix)
    {
        var a = Mat4f.MulWithVec4(matrix, new[] { box.X1, box.Y1, box.Z1, 1f });
        var b = Mat4f.MulWithVec4(matrix, new[] { box.X2, box.Y2, box.Z2, 1f });
        return new(Math.Min(a[0], b[0]), Math.Min(a[1], b[1]), Math.Min(a[2], b[2]),
            Math.Max(a[0], b[0]), Math.Max(a[1], b[1]), Math.Max(a[2], b[2]));
    }
    private static Cuboidf Segment(BlockFacing face)
    {
        var n = face.Normali;
        return new Cuboidf(n.X < 0 ? 0 : .28f, n.Y < 0 ? 0 : .28f, n.Z < 0 ? 0 : .28f,
            n.X > 0 ? 1 : .72f, n.Y > 0 ? 1 : .72f, n.Z > 0 ? 1 : .72f);
    }
}
