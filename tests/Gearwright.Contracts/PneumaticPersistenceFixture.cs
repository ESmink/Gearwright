using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Gearwright.Pneumatics;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.Common.Database;
using static Gearwright.Contracts.PumpRuntimeFixture;

namespace Gearwright.Contracts;

internal static class PneumaticPersistenceFixture
{
    private static readonly ILogger Logger = Stub.Create<ILogger>((_, _) => null);
    private static DbChunk Chunk(int x, int count) => new() { Position = new ChunkPos(x, 2, 3, 0), Data = BitConverter.GetBytes(count) };
    private static GameDatabase Open(string file)
    {
        SQLitePCL.Batteries_V2.Init();
        var db = new GameDatabase(Logger);
        if (!db.OpenConnection(file, 2, false, false)) throw new IOException("Isolated native game database did not open.");
        var native = AccessTools.Field(typeof(GameDatabase), "conn").GetValue(db)!;
        using var command = ((DbConnection)AccessTools.Field(native.GetType(), "sqliteConn").GetValue(native)!).CreateCommand();
        command.CommandText = "PRAGMA journal_mode=DELETE"; command.ExecuteScalar();
        command.CommandText = "PRAGMA synchronous=FULL"; command.ExecuteNonQuery();
        return db;
    }
    private static int Count(GameDatabase db, int x) => BitConverter.ToInt32(db.GetChunk(x, 2, 3), 0);
    internal static int CrashChild(string file, string phase)
    {
        var db = Open(file);
        if (phase == "prepared") Environment.Exit(91);
        IEnumerable<DbChunk> Images()
        {
            yield return Chunk(1, 56);
            if (phase == "first-write") Environment.Exit(91);
            yield return Chunk(2, 8);
        }
        db.SetChunks(Images());
        Environment.Exit(91); // Commit reached disk; deliberately omit Dispose.
        return 91;
    }
    internal static void Run(Action<bool, string> check)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gearwright-pneumatic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var patch = new Harmony("gearwright.tests.pneumatic-store");
        try
        {
            foreach (string phase in new[] { "prepared", "first-write", "committed" })
            {
                string path = Path.Combine(directory, phase + ".vcdbs");
                using (var db = Open(path)) db.SetChunks(new[] { Chunk(1, 64), Chunk(2, 0) });
                var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true };
                start.ArgumentList.Add(typeof(Program).Assembly.Location);
                start.ArgumentList.Add("--pneumatic-crash"); start.ArgumentList.Add(path); start.ArgumentList.Add(phase);
                using var process = Process.Start(start)!;
                if (!process.WaitForExit(15000)) { process.Kill(); throw new TimeoutException("Native crash fixture did not exit."); }
                if (process.ExitCode != 91) throw new InvalidOperationException(process.StandardError.ReadToEnd());
                using var reopened = Open(path);
                int source = Count(reopened, 1), target = Count(reopened, 2);
                check(source + target == 64 && source == (phase == "committed" ? 56 : 64),
                    "Native SQLite process exit at " + phase + " recovers both participants in the same generation");
            }
            PneumaticTransferStore.Install(patch);
            using var database = Open(Path.Combine(directory, "store.vcdbs"));
            int donor = 64, recipient = 0;
            var api = Stub.Create<ICoreServerAPI>((m, _) => m.Name == "get_Logger" ? Logger : null);
            DbChunk[] Capture(IReadOnlyList<BlockPos> _) => new[] { Chunk(1, donor), Chunk(2, recipient) };
            var old = Capture(Array.Empty<BlockPos>());
            database.SetChunks(old);
            var store = new PneumaticTransferStore(api, database, Capture);
            var positions = new[] { new BlockPos(32, 64, 96), new BlockPos(64, 64, 96) };
            check(store.Commit(positions, () => { donor = 56; recipient = 8; }, () => { donor = 64; recipient = 0; }) &&
                Count(database, 1) == 56 && Count(database, 2) == 8,
                "The actual pneumatic transfer store acknowledges a durable native batch");
            database.SetChunks(old);
            check(Count(database, 1) == 56 && Count(database, 2) == 8,
                "Delayed pre-transfer background snapshots cannot resurrect a donor or erase its recipient");
            using (var unrelated = Open(Path.Combine(directory, "unrelated.vcdbs")))
            {
                unrelated.SetChunks(new[] { Chunk(1, 31), Chunk(2, 7) });
                check(Count(unrelated, 1) == 31 && Count(unrelated, 2) == 7 && Count(database, 1) == 56,
                    "Pneumatic checkpoints never replace chunks written to a different world database");
            }
            // Force SQLite to abort after writing one row. The native transaction
            // must roll back every row before the in-memory rollback unlocks.
            var native = AccessTools.Field(typeof(GameDatabase), "conn").GetValue(database)!;
            var connection = (DbConnection)AccessTools.Field(native.GetType(), "sqliteConn").GetValue(native)!;
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER abort_pneumatic BEFORE INSERT ON chunk WHEN NEW.position=" +
                new ChunkPos(2, 2, 3).ToChunkIndex() + " BEGIN SELECT RAISE(ABORT,'injected disk transaction failure'); END";
            command.ExecuteNonQuery();
            check(!store.Commit(positions, () => { donor = 48; recipient = 16; }, () => { donor = 56; recipient = 8; }) &&
                donor == 56 && recipient == 8 && Count(database, 1) == 56 && Count(database, 2) == 8 && !store.Ready,
                "An aborted native write retains one owner, restores memory and disables further extraction");
        }
        finally
        {
            patch.UnpatchAll("gearwright.tests.pneumatic-store"); PneumaticTransferStore.Reset();
            // Exact fixture directory created above; never a caller-provided path.
            Directory.Delete(Path.GetFullPath(directory), true);
        }
    }
}
