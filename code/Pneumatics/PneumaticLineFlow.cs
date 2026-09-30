using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.MathTools;

namespace Gearwright.Pneumatics;

// These are transient snapshots, not registered names or serialized enums.
internal enum PneumaticLineKind { Intake, Tube, Sender, InlineReceiver, EndReceiver, Router }

internal readonly record struct PneumaticPosition(int Dimension, int X, int Y, int Z)
{
    public PneumaticPosition Offset(BlockFacing face) =>
        new(Dimension, X + face.Normali.X, Y + face.Normali.Y, Z + face.Normali.Z);
}

internal readonly record struct PneumaticLineNode(
    PneumaticPosition Position, PneumaticLineKind Kind, BlockFacing? Input, BlockFacing? Output,
    bool Loaded = true, bool Writable = true, BlockFacing[]? Inputs = null, BlockFacing[]? Outputs = null)
{
    public BlockFacing[] Inlets => Kind == PneumaticLineKind.Router ? Inputs ?? Array.Empty<BlockFacing>() :
        Input == null ? Array.Empty<BlockFacing>() : new[] { Input };
    public BlockFacing[] Outlets => Kind == PneumaticLineKind.Router ? Outputs ?? Array.Empty<BlockFacing>() :
        Output == null ? Array.Empty<BlockFacing>() : new[] { Output };
    public bool Valid => Kind switch
    {
        PneumaticLineKind.Intake => Input == null && PneumaticAir.ValidFace(Output),
        PneumaticLineKind.Tube => PneumaticAir.ValidFace(Input) && PneumaticAir.ValidFace(Output) && Input != Output,
        PneumaticLineKind.Sender or PneumaticLineKind.InlineReceiver =>
            PneumaticAir.ValidFace(Input) && Output == Input!.Opposite,
        PneumaticLineKind.EndReceiver => PneumaticAir.ValidFace(Input) && Output == null,
        PneumaticLineKind.Router => Inlets.Concat(Outlets).All(PneumaticAir.ValidFace) &&
            Inlets.Concat(Outlets).Distinct().Count() == Inlets.Length + Outlets.Length &&
            Inlets.Length + Outlets.Length <= 4 && Coplanar(Inlets.Concat(Outlets).ToArray()),
        _ => false
    };
    private static bool Coplanar(BlockFacing[] faces) => Enum.GetValues<EnumAxis>().Any(axis => faces.All(face => face.Axis != axis));

    public double LossPerSecond => Kind switch
    {
        PneumaticLineKind.Intake => 0,
        PneumaticLineKind.Tube => PneumaticAir.TubeLossPerSecond,
        PneumaticLineKind.Router => 2,
        _ => PneumaticAir.EndpointLossPerSecond
    };
}

/// <summary>A root budget already debited from one intake for this interval.</summary>
internal readonly record struct PneumaticSupply(PneumaticPosition Intake, double Amount);

/// <summary>
/// Forwarded air is the remainder of the same root budget, not a fresh supply or
/// an independently spendable movement allowance at each downstream section.
/// </summary>
internal readonly record struct PneumaticSectionFlow(PneumaticPosition Root, double Received, double Consumed, double Forwarded, double Movement = 0,
    BlockFacing? SelectedInput = null);

internal sealed record PneumaticLineFlowResult(
    string Status, IReadOnlyDictionary<PneumaticPosition, PneumaticSectionFlow> Sections,
    double Consumed, double Vented);

/// <summary>
/// Phase-1 direct lines only: no junctions, branches, cargo or persistent caches.
/// Each call starts from explicit, finite root budgets. Missing/unloaded/protected
/// nodes break the line and vent its remainder without forcing chunks to load.
/// </summary>
internal static class PneumaticLineFlow
{
    public static PneumaticLineFlowResult Solve(
        IReadOnlyList<PneumaticLineNode> nodes, IReadOnlyList<PneumaticSupply> supplies, double seconds,
        IReadOnlyDictionary<PneumaticPosition, double>? movement = null)
    {
        if (nodes.Any(n => n.Kind == PneumaticLineKind.Router)) return PneumaticRouterFlow.Solve(nodes, supplies, seconds, movement);
        var flows = new Dictionary<PneumaticPosition, PneumaticSectionFlow>();
        PneumaticLineFlowResult Reject(string status) => new(status, flows, 0, 0);
        if (!PneumaticAir.ValidStep(seconds)) return Reject("invalid-interval");
        if (nodes.Count > PneumaticAir.MaximumNodes || supplies.Count > PneumaticAir.MaximumNodes)
            return Reject("network-limit");

        var topology = new Dictionary<PneumaticPosition, PneumaticLineNode>();
        foreach (var node in nodes)
        {
            if (!node.Valid || !topology.TryAdd(node.Position, node)) return Reject("invalid-topology");
        }

        var roots = new HashSet<PneumaticPosition>();
        foreach (var supply in supplies)
        {
            if (!double.IsFinite(supply.Amount) || supply.Amount < 0 ||
                supply.Amount > PneumaticAir.OutletUnitsPerSecond * seconds ||
                !roots.Add(supply.Intake) || !topology.TryGetValue(supply.Intake, out var root) ||
                root.Kind != PneumaticLineKind.Intake)
                return Reject("invalid-supply");
        }

        double consumed = 0, vented = 0;
        // Stable physical order includes dimension; dictionary/tick order is irrelevant.
        foreach (var supply in supplies.OrderBy(s => s.Intake.Dimension).ThenBy(s => s.Intake.X)
                     .ThenBy(s => s.Intake.Y).ThenBy(s => s.Intake.Z))
        {
            double amount = supply.Amount;
            var position = supply.Intake;
            var visited = new HashSet<PneumaticPosition>();
            while (amount > 0 && topology.TryGetValue(position, out var node) && node.Loaded && node.Writable && visited.Add(position))
            {
                double loss = Math.Min(amount, node.LossPerSecond * seconds);
                double requested = movement != null && movement.TryGetValue(position, out double work) && double.IsFinite(work)
                    ? Math.Max(0, work) : 0;
                double spent = Math.Min(amount - loss, requested);
                double forwarded = amount - loss - spent;
                flows.Add(position, new(supply.Intake, amount, loss, forwarded, spent));
                consumed += loss + spent;
                amount = forwarded;
                if (node.Output == null) break;
                var nextPosition = position.Offset(node.Output);
                if (!topology.TryGetValue(nextPosition, out var next) || next.Input != node.Output.Opposite) break;
                position = nextPosition;
            }
            vented += amount;
        }

        return new("ok", flows, consumed, vented);
    }
}
