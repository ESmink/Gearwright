using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Gearwright.Pneumatics;

/// <summary>Item-dependent, weighted routes over loaded physical port transitions.</summary>
internal sealed class PneumaticRouteGraph
{
    private readonly IReadOnlyDictionary<PneumaticPosition, BlockEntityPneumaticTransport> hosts;
    private readonly System.Func<BlockEntityPneumaticTransport, bool> eligible;
    private readonly System.Func<bool> spend;
    internal bool Exhausted;
    private readonly record struct Step(PneumaticPosition Position, BlockFacing Input);

    internal PneumaticRouteGraph(IReadOnlyDictionary<PneumaticPosition, BlockEntityPneumaticTransport> hosts,
        System.Func<BlockEntityPneumaticTransport, bool> eligible, System.Func<bool> spend)
    { this.hosts = hosts; this.eligible = eligible; this.spend = spend; }
    private bool Spend() { if (spend()) return true; Exhausted = true; return false; }
    internal static BlockFacing? Between(PneumaticPosition from, PneumaticPosition to) =>
        BlockFacing.ALLFACES.FirstOrDefault(f => from.Offset(f) == to);

    internal (BlockEntityPneumaticTransport Host, int Bound)[] Suppliers(BlockEntityPneumaticTransport receiver)
    {
        var best = new Dictionary<PneumaticPosition, int> { [receiver.Position] = 1 };
        var queue = new PriorityQueue<BlockEntityPneumaticTransport, int>(); queue.Enqueue(receiver, 1);
        var result = new List<(BlockEntityPneumaticTransport, int)>();
        while (queue.TryDequeue(out var host, out int cost) && Spend())
        {
            if (cost != best[host.Position]) continue;
            if (host.Kind == PneumaticLineKind.Sender) result.Add((host, cost));
            foreach (var inlet in host.Node.Inlets)
            {
                var p = host.Position.Offset(inlet);
                if (!hosts.TryGetValue(p, out var previous) || !eligible(previous) || !previous.Node.Outlets.Contains(inlet.Opposite)) continue;
                int candidate = cost + (previous.Kind == PneumaticLineKind.Router ? 10 : 1);
                if (best.TryGetValue(p, out int known) && known <= candidate) continue;
                best[p] = candidate; queue.Enqueue(previous, candidate);
            }
        }
        return result.OrderBy(r => r.Item2).ThenBy(r => r.Item1.Position.Dimension).ThenBy(r => r.Item1.Position.X)
            .ThenBy(r => r.Item1.Position.Y).ThenBy(r => r.Item1.Position.Z).ToArray();
    }

    private BlockFacing[] MatchingOutputs(BlockEntityPneumaticTransport host, BlockFacing input, ItemStack stack)
    {
        if (host.Kind != PneumaticLineKind.Router) return host.Node.Outlets;
        var r = host.Router; int entry = r.Port(input);
        var outputs = host.RouterOutputs;
        return outputs.Where(n => r.Allows(stack, entry, n, outputs, true)).Select(r.Face).ToArray();
    }
    private BlockFacing[] Outputs(BlockEntityPneumaticTransport host, BlockFacing input, ItemStack stack, BlockEntityPneumaticTransport receiver, bool committed, bool relaxed)
    {
        if (host.Kind != PneumaticLineKind.Router) return host.Node.Outlets;
        var r = host.Router;
        int[] possible = MatchingOutputs(host, input, stack).Where(f => ReachesReceiver(host, f, stack, receiver, false)).Select(r.Port).ToArray();
        if (possible.Length == 0) return Array.Empty<BlockFacing>();
        if (relaxed) return possible.Select(r.Face).ToArray();
        int priority = possible.Max(n => r.Configuration.Ports[n - 1].SortingPriority);
        possible = possible.Where(n => r.Configuration.Ports[n - 1].SortingPriority == priority).ToArray();
        if (!committed && r.Configuration.Policy == "forced-round-robin") possible = new[] { r.Choose(possible) };
        else if (!committed && r.Configuration.Policy == "round-robin")
        {
            var free = possible.Where(n => ReachesReceiver(host, r.Face(n), stack, receiver, true)).ToArray();
            if (free.Length > 0) possible = free;
            if (possible.Length > 0) possible = new[] { r.Choose(possible) };
        }
        return possible.Select(r.Face).ToArray();
    }

    private bool ReachesReceiver(BlockEntityPneumaticTransport origin, BlockFacing exit, ItemStack stack, BlockEntityPneumaticTransport receiver, bool free)
    {
        var queue = new Queue<Step>(); queue.Enqueue(new(origin.Position.Offset(exit), exit.Opposite));
        // A path may not return through the origin and thereby pretend that an
        // unrelated exit reaches the receiver through the router's other exit.
        var seen = new HashSet<Step>();
        while (queue.TryDequeue(out var step) && Spend())
        {
            if (step.Position == origin.Position || !seen.Add(step) || !hosts.TryGetValue(step.Position, out var h) || !eligible(h) || !h.Node.Inlets.Contains(step.Input)) continue;
            if (h == receiver) return true;
            if (free && (h.State.Cargo != null || h.State.Returning)) continue;
            foreach (var output in MatchingOutputs(h, step.Input, stack)) queue.Enqueue(new(h.Position.Offset(output), output.Opposite));
        }
        return false;
    }

    internal (BlockEntityPneumaticTransport[] Hosts, int Cost)? Find(BlockEntityPneumaticTransport sender,
        BlockEntityPneumaticTransport receiver, ItemStack stack, BlockFacing? input = null, bool committed = false)
    {
        var route = FindCore(sender, receiver, stack, input, committed, false);
        // Interacting priorities in a directed loop can strand a search even
        // though a filtered path exists. Destination identity takes precedence.
        return route ?? (Exhausted ? null : FindCore(sender, receiver, stack, input, committed, true));
    }
    private (BlockEntityPneumaticTransport[] Hosts, int Cost)? FindCore(BlockEntityPneumaticTransport sender,
        BlockEntityPneumaticTransport receiver, ItemStack stack, BlockFacing? input, bool committed, bool relaxed)
    {
        var start = new Step(sender.Position, input ?? sender.State.Input);
        var best = new Dictionary<Step, int> { [start] = 1 };
        var previous = new Dictionary<Step, Step>();
        var queue = new PriorityQueue<Step, (int, int, int, int, int)>();
        void Enqueue(Step s, int cost) => queue.Enqueue(s, (cost, s.Position.X, s.Position.Y, s.Position.Z, s.Input.Index));
        Enqueue(start, 1);
        while (queue.TryDequeue(out var step, out var priority) && Spend())
        {
            if (priority.Item1 != best[step]) continue;
            if (!hosts.TryGetValue(step.Position, out var h) || !eligible(h) ||
                !(committed && step == start && h.Kind == PneumaticLineKind.Router) && !h.Node.Inlets.Contains(step.Input)) continue;
            if (h == receiver)
            {
                var path = new List<BlockEntityPneumaticTransport> { h };
                while (previous.TryGetValue(step, out var before)) { path.Add(hosts[before.Position]); step = before; }
                path.Reverse();
                return path.Count <= PneumaticAir.MaximumNodes ? (path.ToArray(), priority.Item1) : null;
            }
            foreach (var output in Outputs(h, step.Input, stack, receiver, committed, relaxed))
            {
                var next = new Step(h.Position.Offset(output), output.Opposite);
                if (!hosts.TryGetValue(next.Position, out var target) || !eligible(target) || !target.Node.Inlets.Contains(next.Input)) continue;
                int candidate = priority.Item1 + (target.Kind == PneumaticLineKind.Router ? 10 : 1);
                if (best.TryGetValue(next, out int old) && old <= candidate) continue;
                best[next] = candidate; previous[next] = step; Enqueue(next, candidate);
            }
        }
        return null;
    }
}
