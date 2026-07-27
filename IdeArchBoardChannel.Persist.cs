#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

internal static partial class IdeArchBoardChannel
{
    static readonly object Gate = new();

    static BoardDoc Load(SessionContext session)
    {
        lock (Gate)
        {
            var path = LatestPath(session);
            if (!File.Exists(path))
                return new BoardDoc();
            try
            {
                var doc = JsonSerializer.Deserialize<BoardDoc>(File.ReadAllText(path), Pretty);
                return doc ?? new BoardDoc();
            }
            catch
            {
                return new BoardDoc();
            }
        }
    }

    static void Save(SessionContext session, BoardDoc doc)
    {
        lock (Gate)
        {
            doc.UpdatedUtc = DateTimeOffset.UtcNow;
            doc.Schema = SchemaVersion;
            var dir = BoardDir(session);
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(doc, Pretty);
            File.WriteAllText(LatestPath(session), json);
            File.WriteAllText(
                Path.Combine(dir, $"board-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json"),
                json);
        }
    }

    static string LatestPath(SessionContext session) =>
        Path.Combine(BoardDir(session), "LATEST.json");

    static string BoardDir(SessionContext session)
    {
        var root = session.ProjectRoot ?? session.ScmRoot;
        if (root is { Length: > 0 })
            return Path.GetFullPath(Path.Combine(root, ".cdp", "arch-board"));
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp",
            "arch-board");
    }
}
