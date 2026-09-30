using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Vintagestory.API.Common;

namespace Gearwright.Pneumatics;

/// <summary>Direct router links point away from the nearest pipe-fed router.
/// A stable position tie-break keeps both ends consistent without manual roles.</summary>
internal static class PneumaticRouterPortRoles
{
    private sealed class Cache
    {
        internal long Epoch = -1;
        internal readonly Dictionary<BlockEntityPneumaticTransport, int> Distance = new();
    }
    private static readonly ConditionalWeakTable<IWorldAccessor, Cache> caches = new();
    internal static string Between(BlockEntityPneumaticTransport from, BlockEntityPneumaticTransport to)
    {
        var cache = caches.GetOrCreateValue(from.Api.World);
        long epoch = from.Api.World.ElapsedMilliseconds / 100;
        if (cache.Epoch != epoch) { cache.Distance.Clear(); cache.Epoch = epoch; }
        if (!cache.Distance.ContainsKey(from)) Resolve(from, cache);
        if (!cache.Distance.TryGetValue(from, out int a) || !cache.Distance.TryGetValue(to, out int b) || a == int.MaxValue && b == int.MaxValue) return "closed";
        int order = a.CompareTo(b);
        if (order == 0) order = (from.Position.Dimension, from.Position.X, from.Position.Y, from.Position.Z)
            .CompareTo((to.Position.Dimension, to.Position.X, to.Position.Y, to.Position.Z));
        return order < 0 ? "output" : "input";
    }
    private static void Resolve(BlockEntityPneumaticTransport start, Cache cache)
    {
        var links = new Dictionary<BlockEntityPneumaticTransport, List<BlockEntityPneumaticTransport>>();
        var discover = new Queue<BlockEntityPneumaticTransport>(); discover.Enqueue(start);
        var sources = new Queue<BlockEntityPneumaticTransport>();
        while (discover.TryDequeue(out var h) && links.Count < PneumaticAir.MaximumNodes)
        {
            if (links.ContainsKey(h)) continue;
            var adjacent = new List<BlockEntityPneumaticTransport>(); bool fed = false;
            foreach (int n in Enumerable.Range(1, 4))
            {
                var face = h.Router.Face(n).Opposite;
                var neighbour = h.PortNeighbour(n);
                if (neighbour is BlockEntityPneumaticTransport r && r.Kind == PneumaticLineKind.Router && r.Router.Port(face) > 0)
                { adjacent.Add(r); if (!links.ContainsKey(r)) discover.Enqueue(r); }
                else fed |= neighbour is BlockEntityPneumaticAirIntake intake && intake.Outlet == face ||
                    neighbour is BlockEntityPneumaticTransport pipe && pipe.State.Output == face;
            }
            links[h] = adjacent; cache.Distance[h] = fed ? 0 : int.MaxValue;
            if (fed) sources.Enqueue(h);
        }
        while (sources.TryDequeue(out var h))
            foreach (var next in links[h])
                if (links.ContainsKey(next) && cache.Distance[next] > cache.Distance[h] + 1)
                { cache.Distance[next] = cache.Distance[h] + 1; sources.Enqueue(next); }
    }
}
