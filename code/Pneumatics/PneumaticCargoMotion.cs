using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Gearwright.Pneumatics;

/// <summary>Client-only positions shared across a parcel's authoritative host handoffs.</summary>
internal sealed class PneumaticCargoMotion
{
    private static readonly ConditionalWeakTable<IWorldAccessor, PneumaticCargoMotion> worlds = new();
    private readonly Dictionary<string, Sample> parcels = new();
    private sealed class Sample
    {
        internal string Owner = "";
        internal int RouteIndex;
        internal long Started, Seen;
        internal Vec3d From = new(), Target = new();
        internal System.Func<double, long, Vec3d>? Path;
        internal double FromProgress, TargetProgress;
        internal bool Phase;
        internal double ProgressAt(long now) => FromProgress + (TargetProgress - FromProgress) * Math.Clamp((now - Started) / 100.0, 0, 1);
        internal Vec3d At(long now)
        {
            if (Phase && Path != null) return Path(ProgressAt(now), now);
            double t = Math.Clamp((now - Started) / 100.0, 0, 1);
            return new(From.X + (Target.X - From.X) * t, From.Y + (Target.Y - From.Y) * t,
                From.Z + (Target.Z - From.Z) * t);
        }
    }

    internal static PneumaticCargoMotion For(IWorldAccessor world) => worlds.GetValue(world, _ => new());
    internal int Count => parcels.Count;

    internal void Observe(string parcel, string owner, int routeIndex, Vec3d position, long now)
        => Observe(parcel, owner, routeIndex, position, now, null, null);

    // Interpolate the phase of a stopped/rotating mechanism, then evaluate its
    // path. Interpolating endpoint positions would slow a parcel down for an
    // entire packet interval when it crosses the catch or launch boundary.
    internal void ObservePath(string parcel, string owner, int routeIndex, double progress, System.Func<double, long, Vec3d> path, long now)
        => Observe(parcel, owner, routeIndex, path(progress, now), now, progress, path);

    private void Observe(string parcel, string owner, int routeIndex, Vec3d position, long now,
        double? progress, System.Func<double, long, Vec3d>? path)
    {
        if (parcel == "") return;
        if (!parcels.TryGetValue(parcel, out var sample))
        {
            // Old completed parcels need a short grace period when the donor's
            // removal packet arrives before the recipient's packet. No cargo is cached.
            foreach (string stale in parcels.Where(p => now - p.Value.Seen > 5000).Select(p => p.Key).ToArray()) parcels.Remove(stale);
            if (parcels.Count >= 64) parcels.Remove(parcels.MinBy(p => p.Value.Seen).Key);
            parcels[parcel] = new() { Owner = owner, RouteIndex = routeIndex, Started = now, Seen = now,
                From = position.Clone(), Target = position.Clone(), Path = path, Phase = path != null,
                FromProgress = progress ?? 0, TargetProgress = progress ?? 0 };
            return;
        }
        // Independent block packets can arrive in either order. A former host
        // must neither rewind nor draw a parcel already observed downstream.
        if (routeIndex < sample.RouteIndex) return;
        sample.Seen = now;
        bool sameHost = sample.Owner == owner && sample.RouteIndex == routeIndex;
        bool phase = sameHost && path != null && sample.Path != null && progress >= sample.TargetProgress &&
            (sample.Phase || now - sample.Started >= 100);
        if (phase && progress == sample.TargetProgress)
        { sample.Path = path; return; }
        var from = sample.At(now);
        double fromProgress = sample.Phase ? sample.ProgressAt(now) : sample.TargetProgress;
        sample.Owner = owner; sample.RouteIndex = routeIndex;
        if (!phase && path == null && sample.Target.SquareDistanceTo(position) < 1e-16) return;
        sample.From = from; sample.Target = position.Clone(); sample.Started = now;
        sample.Path = path; sample.Phase = phase;
        sample.FromProgress = fromProgress; sample.TargetProgress = progress ?? 0;
    }

    internal bool TryPosition(string parcel, string owner, long now, out Vec3d position)
    {
        if (parcels.TryGetValue(parcel, out var sample) && sample.Owner == owner)
        { position = sample.At(now); return true; }
        position = new(); return false;
    }

    internal void Release(string owner)
    {
        foreach (string parcel in parcels.Where(p => p.Value.Owner == owner).Select(p => p.Key).ToArray()) parcels.Remove(parcel);
    }
}
