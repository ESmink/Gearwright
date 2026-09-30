using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Gearwright.Pneumatics;

/// <summary>Additive instance identity and latest atomic-transfer receipt.</summary>
public sealed class BEBehaviorPneumaticChest : BlockEntityBehavior
{
    private const string Key = "gearwrightPneumaticChest";
    private IAttribute? original;
    public bool Writable { get; private set; } = true;
    public string Instance { get; private set; } = Guid.NewGuid().ToString("N");
    public string Receipt { get; set; } = "";
    public BEBehaviorPneumaticChest(BlockEntity entity) : base(entity) { }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor world)
    {
        base.FromTreeAttributes(tree, world);
        original = tree[Key]?.Clone();
        if (original == null) return;
        Writable = false;
        if (original is ITreeAttribute t && t["schemaVersion"] is IntAttribute && t.GetInt("schemaVersion") == 1 &&
            t["instance"] is StringAttribute && Guid.TryParseExact(t.GetString("instance"), "N", out _) &&
            (!t.HasAttribute("receipt") || t["receipt"] is StringAttribute))
        { Instance = t.GetString("instance"); Receipt = t.GetString("receipt", ""); Writable = true; }
        if (!Writable) world.Logger.Error("[Gearwright] Pneumatic chest receipt at {0} is unreadable; pneumatic access paused and original state preserved.", Pos);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        if (!Writable) { tree[Key] = original!.Clone(); return; }
        var t = original is ITreeAttribute saved ? saved.Clone() : new TreeAttribute();
        t.SetInt("schemaVersion", 1); t.SetString("instance", Instance); t.SetString("receipt", Receipt);
        tree[Key] = t;
    }
}
