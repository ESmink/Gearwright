using System;
using Gearwright.Air;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Gearwright.Pneumatics;

/// <summary>
/// Independent schema for finite intake air and its outlet. A protected document,
/// including a non-tree attribute, is cloned back untouched. No flow cache is saved.
/// </summary>
internal sealed class PneumaticIntakeState
{
    public const int CurrentVersion = 2;
    private IAttribute? original;

    public bool CanWrite { get; private init; } = true;
    public string? Problem { get; private set; }
    public BlockFacing Outlet { get; private set; } = BlockFacing.NORTH;
    public double StoredAir { get; private set; }

    public static PneumaticIntakeState Read(IAttribute? attribute)
    {
        if (attribute == null) return new();
        var state = new PneumaticIntakeState { original = attribute.Clone(), CanWrite = false };
        if (attribute is not ITreeAttribute tree || tree["schemaVersion"] is not IntAttribute)
        {
            state.Problem = "invalid";
            return state;
        }

        int version = tree.GetInt("schemaVersion");
        if (version < 1 || version > CurrentVersion)
        {
            state.Problem = version > CurrentVersion ? "newer" : "unsupported";
            return state;
        }

        // Only optional absent fields receive defaults. Wrong types, faces and
        // out-of-range quantities protect the whole original document.
        BlockFacing? outlet = !tree.HasAttribute("outlet") ? BlockFacing.NORTH :
            tree["outlet"] is StringAttribute ? Array.Find(BlockFacing.ALLFACES, face => face.Code == tree.GetString("outlet")) : null;
        double air = !tree.HasAttribute("storedAir") ? 0 :
            tree["storedAir"] is DoubleAttribute ? tree.GetDouble("storedAir") : double.NaN;
        double limit = version == 1 ? PneumaticAir.LegacyPlenumCapacity : PneumaticAir.PlenumCapacity;
        if (!PneumaticAir.ValidFace(outlet) || !double.IsFinite(air) || air < 0 || air > limit)
        {
            state.Problem = "invalid";
            return state;
        }

        // Schema 1 -> 2: larger capacity, same literal amount and outlet. Do not
        // refill old reservoirs or accept quantities invalid under their schema.
        var migrated = tree.Clone();
        if (version == 1) migrated.SetInt("schemaVersion", 2);
        return new PneumaticIntakeState { original = migrated, Outlet = outlet!, StoredAir = air };
    }

    public IAttribute Write()
    {
        if (!CanWrite) return original!.Clone();
        var tree = original is ITreeAttribute saved ? saved.Clone() : new TreeAttribute();
        tree.SetInt("schemaVersion", CurrentVersion);
        tree.SetString("outlet", Outlet.Code);
        tree.SetDouble("storedAir", StoredAir);
        return tree;
    }

    public bool SetOutlet(BlockFacing? outlet)
    {
        if (!CanWrite || !PneumaticAir.ValidFace(outlet) || outlet == Outlet) return false;
        Outlet = outlet!;
        return true;
    }

    /// <returns>The amount retained; rejected or excess air vents at the intake.</returns>
    public double Receive(AirFlow flow)
    {
        if (!CanWrite || !flow.IsValid || !PneumaticAir.ValidFace(flow.Direction) || flow.Direction.Opposite == Outlet)
            return 0;
        double retained = Math.Min(PneumaticAir.PlenumCapacity - StoredAir, flow.Amount * PneumaticAir.UnitsPerBellowsUnit);
        StoredAir += retained;
        return retained;
    }

    /// <summary>Debit once per common simulation interval, never once per consumer.</summary>
    public double Release(double seconds)
    {
        if (!CanWrite || !PneumaticAir.ValidStep(seconds)) return 0;
        // Exact emptying over this interval for dA/dt = -k*A². The rate
        // decreases continuously as pressure falls and cannot overdraw air.
        double factor = PneumaticAir.OutletUnitsPerSecond / (PneumaticAir.PlenumCapacity * PneumaticAir.PlenumCapacity);
        double released = StoredAir - StoredAir / (1 + factor * StoredAir * seconds);
        StoredAir -= released;
        return released;
    }
}
