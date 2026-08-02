#nullable enable
using System.Text.Json;
using System.Text.RegularExpressions;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=onboard_desk</c> / Meta <c>cdp_onboard</c> — cold-start explore pulse
/// for an open <see cref="SessionContext.ProjectRoot"/> (no ADR required).
/// Not a VS Code Map: entrypoints + top folders + verticals + next[].
/// </summary>
internal static partial class IdeOnboardChannel
{
    public const string SchemaVersion = "onboard/v0";
    public const string ToolName = "cdp_onboard";
    public const string GoName = "onboard_desk";

    static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    static readonly object Gate = new();

    static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".git", "node_modules", ".vs", "packages", "dist", "out",
        "TestResults", ".idea", ".cascade-ide", "publish-release", "publish-debug",
        ".next", "coverage", "artifacts"
    };

    static readonly Regex EntrypointName = new(
        @"^(Program|Startup|Bootstrap|CompositionRoot)|Host|MainWindow|App\.(axaml|xaml)\.cs$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);


    public static string HandleJson(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null) =>
        JsonSerializer.Serialize(Handle(session, args), Pretty);

    public static object Handle(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var op = (Opt(args, "op") ?? Opt(args, "cmd") ?? "scene").Trim().ToLowerInvariant();
        return op switch
        {
            "scan" or "refresh" or "rescan" => Scan(session),
            "clear" => Clear(session),
            _ => Scene(session)
        };
    }

    public static string PulseLine(SessionContext session)
    {
        lock (Gate)
        {
            var doc = LoadUnlocked(session);
            return Pulse(doc);
        }
    }

    public static bool HasScan(SessionContext session)
    {
        lock (Gate)
        {
            var doc = LoadUnlocked(session);
            return doc.Entrypoints.Count > 0 || doc.Verticals.Count > 0;
        }
    }

    /// <summary>Mirror onboard pulse to flat CIDE chrome latch (not EICAS).</summary>
    public static void PublishGlass(SessionContext session)
    {
        lock (Gate)
        {
            var doc = LoadUnlocked(session);
            var active = doc.Entrypoints.Count > 0 || doc.Verticals.Count > 0;
            CideOnboardLatch.Publish(active, Pulse(doc), doc.ProjectName, doc.ProfileHint);
        }
    }


    static object Scene(SessionContext session)
    {
        var doc = Load(session);
        if (doc.Entrypoints.Count == 0 && doc.Verticals.Count == 0 &&
            (session.ProjectRoot ?? session.ScmRoot) is { Length: > 0 })
            return Scan(session);
        return OkCard(session, doc, "scene");
    }

    static object Scan(SessionContext session)
    {
        var root = session.ProjectRoot ?? session.ScmRoot;
        if (root is null or { Length: 0 })
            return Err("project_required", "cdp_open a project first — onboard scans that ProjectRoot");

        root = Path.GetFullPath(root);
        var doc = BuildScan(root);
        Save(session, doc);
        return OkCard(session, doc, "scan");
    }

    static object Clear(SessionContext session)
    {
        lock (Gate)
        {
            var path = LatestPath(session);
            if (File.Exists(path))
                File.Delete(path);
        }

        PublishGlass(session);

        return new
        {
            ok = true,
            schema = SchemaVersion,
            go = GoName,
            tool = ToolName,
            op = "clear",
            pulse = "onboard · cleared",
            hint = "op=scan to rebuild"
        };
    }


    static string Pulse(ScanDoc doc)
    {
        if (doc.ProjectName is null or { Length: 0 })
            return "onboard · empty";
        return
            $"onboard · {doc.ProjectName} · {doc.ProfileHint} · entry={doc.Entrypoints.Count} · vert={doc.Verticals.Count} · docs={(doc.Docs.HasReadme || doc.Docs.AdrCount > 0 ? "yes" : "no")}";
    }

}
