#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

internal static partial class IdeArchBoardChannel
{
    static readonly object Gate = new();

    /// <summary>Pulse for desk next[] without full card.</summary>
    public static string PulseLine(SessionContext session)
    {
        lock (Gate)
        {
            var asBuilt = LoadUnlockedAt(AsBuiltPath(session));
            if (asBuilt.Mode == "as_built" && asBuilt.Roles.Count > 0)
                return Pulse(asBuilt);
            return Pulse(LoadUnlocked(session));
        }
    }

    /// <summary>True when board has open/elected work — hint on slim desk.</summary>
    public static bool HasActiveWork(SessionContext session)
    {
        lock (Gate)
        {
            var doc = LoadUnlocked(session);
            return doc.Roles.Any(r => r.Status is "open" or "elected");
        }
    }

    static BoardDoc Load(SessionContext session)
    {
        lock (Gate)
            return LoadUnlocked(session);
    }

    static void Save(SessionContext session, BoardDoc doc)
    {
        lock (Gate)
            SaveUnlocked(session, doc);
    }

    /// <summary>Load → optional save under one lock (no TOCTOU race on LATEST.json).</summary>
    static object Mutate(SessionContext session, Func<BoardDoc, (bool Save, object Card)> fn)
    {
        lock (Gate)
        {
            var doc = LoadUnlocked(session);
            var (save, card) = fn(doc);
            if (save)
                SaveUnlocked(session, doc);
            return card;
        }
    }

    static BoardDoc LoadAsBuilt(SessionContext session)
    {
        lock (Gate)
            return LoadUnlockedAt(AsBuiltPath(session));
    }

    static void SaveAsBuilt(SessionContext session, BoardDoc doc)
    {
        lock (Gate)
            SaveUnlockedAt(session, doc, AsBuiltPath(session), stampPrefix: "as-built");
    }

    static BoardDoc LoadUnlocked(SessionContext session) =>
        LoadUnlockedAt(LatestPath(session));

    static BoardDoc LoadUnlockedAt(string path)
    {
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

    static void SaveUnlocked(SessionContext session, BoardDoc doc) =>
        SaveUnlockedAt(session, doc, LatestPath(session), stampPrefix: "board");

    static void SaveUnlockedAt(SessionContext session, BoardDoc doc, string path, string stampPrefix)
    {
        doc.UpdatedUtc = DateTimeOffset.UtcNow;
        doc.Schema = SchemaVersion;
        var dir = BoardDir(session);
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(doc, Pretty);
        File.WriteAllText(path, json);
        File.WriteAllText(
            Path.Combine(dir, $"{stampPrefix}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json"),
            json);
    }

    static string LatestPath(SessionContext session) =>
        Path.Combine(BoardDir(session), "LATEST.json");

    static string AsBuiltPath(SessionContext session) =>
        Path.Combine(BoardDir(session), "AS_BUILT.json");

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
