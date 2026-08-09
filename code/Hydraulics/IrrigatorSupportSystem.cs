using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Gearwright.Hydraulics;

/// <summary>
/// Server-authoritative support layout for straight Irrigator Pipe runs. The
/// solver is deterministic, chunk-aware, and removes at most one invalid pipe
/// per tick before recalculating the resulting runs.
/// </summary>
public sealed class IrrigatorSupportSystem : ModSystem
{
    private const int TickIntervalMilliseconds = 500;
    private readonly Dictionary<BlockPos, BlockEntityIrrigatorPipe> loaded = new();
    private ICoreServerAPI? sapi;
    private long tickListenerId;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;
        tickListenerId = api.Event.RegisterGameTickListener(
            OnServerTick, TickIntervalMilliseconds, TickIntervalMilliseconds / 2);
    }

    public void Register(BlockEntityIrrigatorPipe pipe) => loaded[pipe.Pos.Copy()] = pipe;

    public void Unregister(BlockEntityIrrigatorPipe pipe) => loaded.Remove(pipe.Pos);

    public override void Dispose()
    {
        if (sapi != null && tickListenerId != 0)
            sapi.Event.UnregisterGameTickListener(tickListenerId);
        loaded.Clear();
        sapi = null;
    }

    private void OnServerTick(float _)
    {
        if (sapi == null || loaded.Count == 0) return;
        HashSet<BlockPos> visited = new();
        foreach (BlockEntityIrrigatorPipe seed in loaded.Values.ToArray())
        {
            if (visited.Contains(seed.Pos)) continue;
            List<BlockEntityIrrigatorPipe> line = DiscoverLine(seed, visited);
            if (line.Count == 0 || !LineEndsAreLoaded(line)) continue;

            bool[] eligible = line.Select(HasCeilingSupport).ToArray();
            if (!IrrigatorSupportPlanner.TryPlan(
                    line.Count, eligible, out int[] masks, out int breakIndex))
            {
                BlockEntityIrrigatorPipe problem = line[breakIndex];
                sapi.Logger.Notification(
                    "[Gearwright] Irrigator Pipe at {0} broke off because its ceiling supports could not satisfy the three-pipe span limit.",
                    problem.Pos);
                sapi.World.BlockAccessor.BreakBlock(problem.Pos, null, 1f);
                return;
            }

            for (int i = 0; i < line.Count; i++) line[i].SetSupportMask(masks[i]);
        }
    }

    private List<BlockEntityIrrigatorPipe> DiscoverLine(
        BlockEntityIrrigatorPipe seed,
        HashSet<BlockPos> visited)
    {
        BlockFacing negative = seed.NegativeFace;
        BlockEntityIrrigatorPipe first = seed;
        while (TryGetMatching(first.Pos.AddCopy(negative), seed.AlongX, out BlockEntityIrrigatorPipe prior))
            first = prior;

        List<BlockEntityIrrigatorPipe> result = new();
        BlockEntityIrrigatorPipe current = first;
        while (true)
        {
            result.Add(current);
            visited.Add(current.Pos.Copy());
            if (!TryGetMatching(
                    current.Pos.AddCopy(current.PositiveFace), seed.AlongX,
                    out BlockEntityIrrigatorPipe next)) break;
            current = next;
        }
        return result;
    }

    private bool TryGetMatching(
        BlockPos pos,
        bool alongX,
        out BlockEntityIrrigatorPipe pipe)
    {
        if (loaded.TryGetValue(pos, out BlockEntityIrrigatorPipe? found) && found.AlongX == alongX)
        {
            pipe = found;
            return true;
        }
        pipe = null!;
        return false;
    }

    private bool LineEndsAreLoaded(IReadOnlyList<BlockEntityIrrigatorPipe> line) =>
        sapi!.World.BlockAccessor.GetChunkAtBlockPos(
            line[0].Pos.AddCopy(line[0].NegativeFace)) != null &&
        sapi.World.BlockAccessor.GetChunkAtBlockPos(
            line[^1].Pos.AddCopy(line[^1].PositiveFace)) != null;

    private bool HasCeilingSupport(BlockEntityIrrigatorPipe pipe) =>
        sapi!.World.BlockAccessor.IsSideSolid(
            pipe.Pos.X, pipe.Pos.Y + 1, pipe.Pos.Z, BlockFacing.DOWN);

}
