using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.Common.Database;
using Vintagestory.Server;

namespace Gearwright.Pneumatics;

internal interface IPneumaticTransferStore
{
    bool Ready { get; }
    bool Commit(IReadOnlyList<BlockPos> positions, Action apply, Action rollback);
}

/// <summary>
/// 1.22.3 adapter: native SetChunks commits every participant in one SQLite
/// transaction. FULL synchronous commits are the acknowledgement; MarkDirty is
/// presentation only. No independently replayable cargo images are created.
/// </summary>
internal sealed class PneumaticTransferStore : IPneumaticTransferStore
{
    internal static readonly object Gate = new();
    private sealed record Stamp(long Epoch, long Sequence);
    private sealed record Checkpoint(long Epoch, long Sequence, byte[] Bytes);
    private static ConditionalWeakTable<byte[], Stamp> snapshots = new();
    private static readonly Dictionary<ulong, Checkpoint> checkpoints = new();
    private static long epoch;
    private static long sequence;
    private static GameDatabase? activeDatabase;
    private readonly ICoreServerAPI api;
    private readonly GameDatabase database;
    private readonly object transactionLock;
    private readonly DbConnection connection;
    private readonly System.Func<IReadOnlyList<BlockPos>, DbChunk[]?> capture;
    public bool Ready { get; private set; } = true;

    private static GameDatabase Database(ICoreServerAPI api)
    {
        var thread = AccessTools.Field(api.World.GetType(), "chunkThread").GetValue(api.World)!;
        return (GameDatabase)AccessTools.Field(thread.GetType(), "gameDatabase").GetValue(thread)!;
    }

    internal PneumaticTransferStore(ICoreServerAPI api) : this(api, Database(api), positions =>
    {
        var chunks = new Dictionary<ulong, (ChunkPos Pos, ServerChunk Chunk)>();
        foreach (var p in positions)
        {
            if (api.WorldManager.GetChunk(p) is not ServerChunk chunk || chunk.Disposed) return null;
            var pos = new ChunkPos(p.X >> 5, p.Y >> 5, p.Z >> 5, p.dimension);
            chunks[pos.ToChunkIndex()] = (pos, chunk);
        }
        using var stream = new FastMemoryStream();
        return chunks.Values.Select(c => new DbChunk { Position = c.Pos, Data = c.Chunk.ToBytes(stream) }).ToArray();
    }) { }

    internal PneumaticTransferStore(ICoreServerAPI api, GameDatabase database, System.Func<IReadOnlyList<BlockPos>, DbChunk[]?> capture)
    {
        this.api = api; this.database = database; this.capture = capture;
        var native = AccessTools.Field(typeof(GameDatabase), "conn").GetValue(database)!;
        if (native.GetType().FullName != "Vintagestory.Common.Database.SQLiteDbConnectionv2")
            throw new NotSupportedException("Pneumatic transfers require the verified version-2 game database adapter.");
        transactionLock = AccessTools.Field(native.GetType(), "transactionLock").GetValue(native)!;
        connection = (DbConnection)AccessTools.Field(native.GetType(), "sqliteConn").GetValue(native)!;
        lock (Gate) lock (transactionLock)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode";
            string mode = Convert.ToString(command.ExecuteScalar()) ?? "";
            if (mode is "off" or "memory")
            {
                // The stock engine can select MEMORY for performance. A disk
                // rollback journal is required for item ownership transactions;
                // use DELETE to keep the save a single database between writes.
                command.CommandText = "PRAGMA journal_mode=DELETE";
                mode = Convert.ToString(command.ExecuteScalar()) ?? "";
            }
            if (mode is not ("delete" or "truncate" or "persist" or "wal"))
                throw new NotSupportedException("The game database could not establish a durable transaction journal.");
            if (activeDatabase != null && !ReferenceEquals(activeDatabase, database))
                throw new NotSupportedException("Pneumatic transfers already belong to another active world database.");
            activeDatabase = database;
        }
    }

    internal static void Install(Harmony harmony)
    {
        // Serialize and persist under the same gate as ownership changes. A save
        // may hold serialized bytes between these calls; its epoch prevents a
        // delayed old donor snapshot from resurrecting extracted goods.
        harmony.Patch(AccessTools.Method(typeof(ServerChunk), "ToBytes", new[] { typeof(FastMemoryStream) }),
            prefix: new HarmonyMethod(typeof(PneumaticTransferStore), nameof(LockSnapshot)),
            postfix: new HarmonyMethod(typeof(PneumaticTransferStore), nameof(StampSnapshot)),
            finalizer: new HarmonyMethod(typeof(PneumaticTransferStore), nameof(Unlock)));
        harmony.Patch(AccessTools.Method(typeof(GameDatabase), nameof(GameDatabase.SetChunks)),
            prefix: new HarmonyMethod(typeof(PneumaticTransferStore), nameof(BeforeSave)),
            postfix: new HarmonyMethod(typeof(PneumaticTransferStore), nameof(AfterSave)),
            finalizer: new HarmonyMethod(typeof(PneumaticTransferStore), nameof(Unlock)));
    }

    private static void LockSnapshot() => Monitor.Enter(Gate);
    private static void StampSnapshot(byte[] __result) { snapshots.Remove(__result); snapshots.Add(__result, new(epoch, ++sequence)); }
    private static Exception? Unlock(Exception? __exception) { Monitor.Exit(Gate); return __exception; }

    private static void BeforeSave(GameDatabase __instance, ref IEnumerable<DbChunk> __0)
    {
        Monitor.Enter(Gate);
        if (!ReferenceEquals(__instance, activeDatabase)) return;
        __0 = __0.Select(chunk =>
        {
            ulong key = chunk.Position.ToChunkIndex();
            if (checkpoints.TryGetValue(key, out var checkpoint) &&
                (!snapshots.TryGetValue(chunk.Data, out var stamp) || stamp.Epoch < checkpoint.Epoch || stamp.Sequence < checkpoint.Sequence))
                return new DbChunk { Position = chunk.Position, Data = checkpoint.Bytes };
            return chunk;
        }).ToArray();
    }

    private static void AfterSave(GameDatabase __instance, IEnumerable<DbChunk> __0)
    {
        if (!ReferenceEquals(__instance, activeDatabase)) return;
        foreach (var c in __0)
            if (checkpoints.TryGetValue(c.Position.ToChunkIndex(), out var previous) && snapshots.TryGetValue(c.Data, out var stamp))
                checkpoints[c.Position.ToChunkIndex()] = new(Math.Max(previous.Epoch, stamp.Epoch), stamp.Sequence, c.Data);
    }

    public bool Commit(IReadOnlyList<BlockPos> positions, Action apply, Action rollback)
    {
        if (!Ready || positions.Count == 0 || positions.Count > 8) return false;
        lock (Gate)
        {
            var before = capture(positions);
            if (before == null) return false;
            // Bounded retained checkpoints. Unknown unbounded worlds stop new
            // dispatch instead of accumulating an unlimited in-memory journal.
            if (checkpoints.Count + before.Count(c => !checkpoints.ContainsKey(c.Position.ToChunkIndex())) > 1024) return false;
            DbChunk[] Snapshot()
            {
                var result = capture(positions) ?? throw new InvalidOperationException("A transfer participant unloaded while committing.");
                foreach (var chunk in result) StampSnapshot(chunk.Data);
                return result;
            }
            // Baseline is used only to restore memory after an aborted database
            // transaction. Participant rollback retains complete native trees.
            lock (transactionLock)
            {
                using var pragma = connection.CreateCommand();
                pragma.CommandText = "PRAGMA synchronous";
                int oldSync = Convert.ToInt32(pragma.ExecuteScalar());
                pragma.CommandText = "PRAGMA synchronous=FULL";
                pragma.ExecuteNonQuery();
                bool applied = false;
                DbChunk[]? after = null;
                try
                {
                    epoch++;
                    applied = true;
                    apply();
                    after = Snapshot();
                    database.SetChunks(after);
                    foreach (var chunk in after) checkpoints[chunk.Position.ToChunkIndex()] = new(epoch, snapshots.GetValue(chunk.Data, _ => new(epoch, ++sequence)).Sequence, chunk.Data);
                    return true;
                }
                catch (Exception error)
                {
                    // Resolve an uncertain commit by exact byte identity, never
                    // by item count. A third state protects all subsequent work.
                    bool committed = after != null && after.All(c =>
                        (database.GetChunk(c.Position.X, c.Position.Y, c.Position.Z, c.Position.Dimension) ?? Array.Empty<byte>()).SequenceEqual(c.Data));
                    if (committed)
                    {
                        foreach (var chunk in after!) checkpoints[chunk.Position.ToChunkIndex()] = new(epoch, snapshots.GetValue(chunk.Data, _ => new(epoch, ++sequence)).Sequence, chunk.Data);
                        return true;
                    }
                    if (applied) rollback();
                    // Lock out further simulation on any write failure. The
                    // engine's original data and native transaction stay intact.
                    Ready = false;
                    api.Logger.Error("[Gearwright] Pneumatic durable transfer failed; transport paused: {0}", error);
                    return false;
                }
                finally
                {
                    pragma.CommandText = "PRAGMA synchronous=" + oldSync;
                    pragma.ExecuteNonQuery();
                }
            }
        }
    }

    internal static void Reset()
    {
        lock (Gate) { checkpoints.Clear(); snapshots = new(); epoch = 0; sequence = 0; activeDatabase = null; }
    }
}
