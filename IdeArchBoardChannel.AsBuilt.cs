#nullable enable
using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>
/// <c>op=as_built</c> — scan open <see cref="SessionContext.ProjectRoot"/> and write
/// <c>.cdp/arch-board/AS_BUILT.json</c> (plan board <c>LATEST.json</c> stays untouched).
/// Profiles: <c>cide</c> (Cockpit+IdeDisplay), <c>cdp_desk</c> (IdeCockpit peels).
/// </summary>
internal static partial class IdeArchBoardChannel
{
    static object AsBuilt(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var root = session.ProjectRoot ?? session.ScmRoot;
        if (root is null or { Length: 0 })
            return Err("project_required", "cdp_open a project first — as_built scans that ProjectRoot");

        root = Path.GetFullPath(root);
        var profile = ResolveArchProfile(root, args);
        var name = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var doc = profile switch
        {
            "cide" => BuildCideAsBuilt(root, name),
            "cdp_desk" => BuildCdpDeskAsBuilt(root, name),
            _ => BuildUnknownAsBuilt(root, name, profile)
        };

        SaveAsBuilt(session, doc);
        return OkCard(
            session,
            doc,
            "as_built",
            pulse: $"as_built · {profile} · {doc.Roles.Count} roles · {doc.Edges.Count} edges · {name}",
            boardPath: AsBuiltPath(session),
            primaryGo: new
            {
                go = GoName,
                label = "Scene as-built",
                why = "op=scene view=as_built"
            });
    }

    /// <summary>Explicit <c>profile=cide|cdp_desk</c> wins; else auto-detect from tree.</summary>
    static string ResolveArchProfile(string root, IReadOnlyDictionary<string, JsonElement> args)
    {
        var forced = (Opt(args, "profile") ?? Opt(args, "arch_profile") ?? "").Trim().ToLowerInvariant();
        if (forced is "cide" or "cdp_desk" or "unknown")
            return forced;
        return DetectArchProfile(root);
    }

    static string DetectArchProfile(string root)
    {
        // Desk peels win even when Cockpit/Channels+Cds exist (hybrid cdp-mcp).
        // Otherwise Cds alone auto-picks cide and silently drops DeskIngestionBus / Instrument.
        if (File.Exists(Path.Combine(root, "IdeCockpit.cs")) &&
            File.Exists(Path.Combine(root, "IdeCockpit.Build.cs")))
            return "cdp_desk";

        var cockpit = Path.Combine(root, "Cockpit");
        var hasCockpit =
            Directory.Exists(Path.Combine(cockpit, "Channels")) &&
            Directory.Exists(Path.Combine(cockpit, "Composition"));
        var hasIds =
            Directory.Exists(Path.Combine(root, "IdeDisplay")) ||
            Directory.Exists(Path.Combine(cockpit, "Cds"));
        if (hasCockpit && hasIds)
            return "cide";

        return "unknown";
    }

}
