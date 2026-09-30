using System;
using Gearwright.Pneumatics;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using static Gearwright.Contracts.PumpRuntimeFixture;

namespace Gearwright.Contracts;

internal static class PneumaticPresentationFixture
{
    internal static void Run(Action<bool, string> check)
    {
        var motion = new PneumaticCargoMotion();
        motion.Observe("parcel", "upstream", 0, new(.6, .5, .5), 0);
        motion.Observe("parcel", "upstream", 0, new(.8, .5, .5), 100);
        motion.TryPosition("parcel", "upstream", 150, out var approach);
        motion.TryPosition("parcel", "upstream", 200, out var before);
        motion.Observe("parcel", "downstream", 1, new(1, .5, .5), 200);
        motion.TryPosition("parcel", "downstream", 200, out var handoff);
        motion.TryPosition("parcel", "downstream", 250, out var crossing);
        motion.TryPosition("parcel", "downstream", 300, out var boundary);
        motion.Observe("parcel", "downstream", 1, new(1.2, .5, .5), 300);
        motion.TryPosition("parcel", "downstream", 350, out var departed);
        check(Near(approach.X, .7) && Near(before.X, .8) && Near(handoff.X, .8) &&
            Near(crossing.X, .9) && Near(boundary.X, 1) && Near(departed.X, 1.1),
            "Cargo advances equal distances before, during and after a pipe handoff without a jump or pause");

        motion.Observe("parcel", "downstream", 1, new(1.2, .5, .5), 350);
        motion.Observe("parcel", "upstream", 0, new(.8, .5, .5), 350);
        motion.TryPosition("parcel", "downstream", 400, out var settled);
        check(Near(settled.X, 1.2) && !motion.TryPosition("parcel", "upstream", 400, out _),
            "Duplicate snapshots do not restart interpolation and delayed former-host packets neither rewind nor duplicate cargo");
        motion.TryPosition("parcel", "downstream", 10000, out var held);
        check(Near(held.X, 1.2), "Without authoritative progress, cargo stays at its last reported position");
        motion.Release("upstream");
        check(motion.TryPosition("parcel", "downstream", 400, out _),
            "Unloading the donor after handoff does not erase the recipient's motion");
        motion.Release("downstream");
        check(motion.Count == 0, "Unloading the current owner releases its presentation record");

        for (int i = 0; i < 80; i++) motion.Observe("parcel-" + i, "host-" + i, 0, new(i, .5, .5), i);
        check(motion.Count == 64, "The presentation cache stays bounded after many completed parcels");
        motion.Observe("fresh", "fresh-host", 0, new(), 6000);
        check(motion.Count == 1, "Expired handoff records are reclaimed before tracking another parcel");
        var firstWorld = Stub.Create<IWorldAccessor>((_, _) => null);
        var secondWorld = Stub.Create<IWorldAccessor>((_, _) => null);
        check(ReferenceEquals(PneumaticCargoMotion.For(firstWorld), PneumaticCargoMotion.For(firstWorld)) &&
            !ReferenceEquals(PneumaticCargoMotion.For(firstWorld), PneumaticCargoMotion.For(secondWorld)),
            "Parcel presentation is shared by renderers within one world and isolated from other worlds");

        PneumaticAirStreamFixture.Run(check);
        AirReadsLoadedCells(check);
    }

    private static void AirReadsLoadedCells(Action<bool, string> check)
    {
        bool loaded = true;
        int blockReads = 0;
        var block = new Block { BlockId = 0 };
        BlockEntity? entity = null;
        var chunk = Stub.Create<IWorldChunk>((_, _) => null);
        var accessor = Stub.Create<IBlockAccessor>((m, _) => m.Name switch
        {
            "GetChunkAtBlockPos" => loaded ? chunk : null,
            "GetBlockEntity" => entity,
            "GetBlock" => ++blockReads > 0 ? block : null,
            _ => null
        });
        var world = Stub.Create<IWorldAccessor>((m, _) => m.Name == "get_BlockAccessor" ? accessor : null);
        var position = new PneumaticPosition(0, 0, 0, 0);
        check(PneumaticAirSpecks.ReadCell(world, position)?.Open == true, "Loaded empty space permits an open air outlet");
        block.BlockId = 1;
        check(PneumaticAirSpecks.ReadCell(world, position) == null, "Any occupied block cell stops decorative airflow");
        block.BlockId = 0; entity = new BlockEntityGenericTypedContainer();
        check(PneumaticAirSpecks.ReadCell(world, position) == null, "Inventory entities never count as open exhaust space");
        var host = new BlockEntityPneumaticTransport { Air = 1, Pos = new(0, 0, 0) };
        Set(host, "Block", new Block { Code = new("gearwright:pneumatic-tube") });
        host.State.Input = BlockFacing.WEST; host.State.Output = BlockFacing.UP; entity = host;
        check(PneumaticAirSpecks.ReadCell(world, position) is { Output: var output } && output == BlockFacing.UP,
            "The airflow reader uses the actual powered tube's current bend");
        host.Air = 0;
        check(PneumaticAirSpecks.ReadCell(world, position) == null, "Unpowered tubes terminate the visible airflow path");
        host.Air = 1; host.State.Output = BlockFacing.EAST;
        foreach (string kind in new[] { "sender", "receiver" })
        {
            Set(host, "Block", new Block { Code = new("gearwright:pneumatic-" + kind) });
            check(PneumaticAirSpecks.ReadCell(world, position)?.Output == BlockFacing.EAST,
                "Air streams continue through the " + kind + " mainline");
        }
        entity = null; loaded = false; blockReads = 0;
        check(PneumaticAirSpecks.ReadCell(world, position) == null && blockReads == 0,
            "Unloaded chunks end the particle path before any block access");
    }

    private static bool Near(double actual, double expected) => Math.Abs(actual - expected) < 1e-8;
}
