using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.MathTools;

namespace Gearwright.Pneumatics;

/// <summary>Conservative graph flow: select one inlet, then divide its remainder once.</summary>
internal static class PneumaticRouterFlow
{
    private readonly record struct Budget(PneumaticPosition Root, BlockFacing? Face, double Amount);
    internal static PneumaticLineFlowResult Solve(IReadOnlyList<PneumaticLineNode> nodes,
        IReadOnlyList<PneumaticSupply> supplies, double seconds, IReadOnlyDictionary<PneumaticPosition, double>? movement)
    {
        var flows = new Dictionary<PneumaticPosition, PneumaticSectionFlow>();
        PneumaticLineFlowResult Reject(string reason) => new(reason, flows, 0, 0);
        if (!PneumaticAir.ValidStep(seconds)) return Reject("invalid-interval");
        if (nodes.Count > PneumaticAir.MaximumNodes || supplies.Count > PneumaticAir.MaximumNodes) return Reject("network-limit");
        var topology = new Dictionary<PneumaticPosition, PneumaticLineNode>();
        foreach (var n in nodes) if (!n.Valid || !topology.TryAdd(n.Position, n)) return Reject("invalid-topology");
        var budgets = topology.Keys.ToDictionary(p => p, _ => new List<Budget>());
        var pending = topology.Keys.ToDictionary(p => p, _ => 0);
        var edges = topology.Keys.ToDictionary(p => p, _ => new List<(PneumaticPosition To, BlockFacing Face)>());
        foreach (var n in nodes.Where(n => n.Loaded && n.Writable))
            foreach (var output in n.Outlets)
            {
                var next = n.Position.Offset(output);
                if (!topology.TryGetValue(next, out var target) || !target.Loaded || !target.Writable ||
                    !target.Inlets.Contains(output.Opposite)) continue;
                edges[n.Position].Add((next, output.Opposite));
            }
        var roots = new HashSet<PneumaticPosition>();
        foreach (var supply in supplies)
        {
            if (!double.IsFinite(supply.Amount) || supply.Amount < 0 || supply.Amount > PneumaticAir.OutletUnitsPerSecond * seconds ||
                !roots.Add(supply.Intake) || !topology.TryGetValue(supply.Intake, out var root) || root.Kind != PneumaticLineKind.Intake)
                return Reject("invalid-supply");
            budgets[supply.Intake].Add(new(supply.Intake, null, supply.Amount));
        }
        // A bypass cannot recirculate the same finite budget. Cut only DFS back
        // edges, starting at powered roots, so exits from a loop still receive
        // air. The chosen forest is deterministic and needs no persistent IDs.
        var colour = topology.Keys.ToDictionary(p => p, _ => 0);
        void Visit(PneumaticPosition p)
        {
            colour[p] = 1;
            for (int i = 0; i < edges[p].Count;)
            {
                var edge = edges[p][i];
                if (colour[edge.To] == 1) { edges[p].RemoveAt(i); continue; }
                if (colour[edge.To] == 0) Visit(edge.To);
                i++;
            }
            colour[p] = 2;
        }
        foreach (var p in supplies.OrderByDescending(s => s.Amount).ThenBy(s => s.Intake.Dimension).ThenBy(s => s.Intake.X)
            .ThenBy(s => s.Intake.Y).ThenBy(s => s.Intake.Z).Select(s => s.Intake)
            .Concat(topology.Keys.OrderBy(p => p.Dimension).ThenBy(p => p.X).ThenBy(p => p.Y).ThenBy(p => p.Z)))
            if (colour[p] == 0) Visit(p);
        foreach (var list in edges.Values) foreach (var edge in list) pending[edge.To]++;
        var queue = new PriorityQueue<PneumaticPosition, (int, int, int, int)>();
        foreach (var p in pending.Keys.Where(p => pending[p] == 0)) queue.Enqueue(p, (p.Dimension, p.X, p.Y, p.Z));
        double consumed = 0, vented = 0;
        while (queue.TryDequeue(out var position, out _))
        {
            var node = topology[position];
            var incoming = budgets[position];
            if (incoming.Count > 0)
            {
                var selected = incoming.OrderByDescending(b => b.Amount).ThenBy(b => Array.IndexOf(node.Inlets, b.Face)).First();
                vented += incoming.Sum(b => b.Amount) - selected.Amount;
                double loss = node.Loaded && node.Writable ? Math.Min(selected.Amount, node.LossPerSecond * seconds) : 0;
                double work = movement != null && movement.TryGetValue(position, out double requested) && double.IsFinite(requested)
                    ? Math.Max(0, requested) : 0;
                double spent = node.Loaded && node.Writable ? Math.Min(selected.Amount - loss, work) : 0;
                double rest = selected.Amount - loss - spent;
                consumed += loss + spent;
                if (node.Loaded && node.Writable)
                {
                    flows[position] = new(selected.Root, selected.Amount, loss, rest, spent, selected.Face);
                    if (edges[position].Count > 0)
                        foreach (var edge in edges[position]) budgets[edge.To].Add(new(selected.Root, edge.Face, rest / edges[position].Count));
                    else vented += rest;
                }
                else vented += rest;
            }
            foreach (var edge in edges[position]) if (--pending[edge.To] == 0)
                queue.Enqueue(edge.To, (edge.To.Dimension, edge.To.X, edge.To.Y, edge.To.Z));
        }
        return new("ok", flows, consumed, vented);
    }
}
