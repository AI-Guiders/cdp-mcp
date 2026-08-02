using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace CdpMcp.IntentWorkspace;

internal sealed partial class IntentWorkspaceStore
{
    /// <summary>Existing WitDB may predate script_last_run — EnsureCreated won't alter.</summary>
    public void EnsureScriptLastRunTable()
    {
        WithDb(db =>
        {
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE IF NOT EXISTS script_last_run (
                    RootKey TEXT NOT NULL PRIMARY KEY,
                    Path TEXT NOT NULL,
                    Mode TEXT NOT NULL,
                    Ok INTEGER NOT NULL,
                    AtUtc TEXT NOT NULL,
                    Pulse TEXT NOT NULL,
                    BodyJson TEXT NULL,
                    BoardJson TEXT NOT NULL
                );
                """);
        });
    }

    public void ScriptLastRunSave(string rootKey, string path, string mode, bool ok, DateTime atUtc, string pulse, string? bodyJson, IReadOnlyList<string> board)
    {
        var boardJson = JsonSerializer.Serialize(board);
        var at = new DateTimeOffset(DateTime.SpecifyKind(atUtc, DateTimeKind.Utc));
        WithDb(db =>
        {
            var row = db.ScriptLastRuns.Find(rootKey);
            if (row is null)
            {
                db.ScriptLastRuns.Add(new ScriptLastRunEntity { RootKey = rootKey, Path = path, Mode = mode, Ok = ok, AtUtc = at, Pulse = pulse, BodyJson = bodyJson, BoardJson = boardJson });
            }
            else
            {
                row.Path = path;
                row.Mode = mode;
                row.Ok = ok;
                row.AtUtc = at;
                row.Pulse = pulse;
                row.BodyJson = bodyJson;
                row.BoardJson = boardJson;
            }

            db.SaveChanges();
        });
    }

    public (string Path, string Mode, bool Ok, DateTime AtUtc, string Pulse, string? BodyJson, string[] Board)? ScriptLastRunTryLoad(string rootKey)
    {
        return WithDb<(string Path, string Mode, bool Ok, DateTime AtUtc, string Pulse, string? BodyJson, string[] Board)?>(db =>
        {
            var row = db.ScriptLastRuns.AsNoTracking().FirstOrDefault(x => x.RootKey == rootKey);
            if (row is null)
                return null;
            string[] board;
            try
            {
                board = JsonSerializer.Deserialize<string[]>(row.BoardJson) ?? [];
            }
            catch
            {
                board = [row.Pulse];
            }

            return (row.Path, row.Mode, row.Ok, row.AtUtc.UtcDateTime, row.Pulse, row.BodyJson, board);
        });
    }
}