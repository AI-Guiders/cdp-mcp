namespace CdpMcp;

/// <summary>
/// World attention channel (ADR 0193): git / shell / browser / mcp — replace-in-seat without organ thrash.
/// Seat pulse and scene-only <c>go=</c> reuse cockpit snaps; <c>pane_full=</c> / <c>go_detail=full</c> still dumps.
/// </summary>
internal static class IdeWorldChannel
{
    public static bool IsWorldOrgan(string? organ)
    {
        if (string.IsNullOrWhiteSpace(organ)) return false;
        var o = IdeCockpit.CanonicalOrganPin(organ);
        return o is "git_scene" or "shell_scene" or "browser" or "mcp_scene";
    }

    /// <summary>Scene verbs that only place + pulse — not search/run/draft mutations.</summary>
    public static bool IsWorldSceneGo(string? verb)
    {
        if (string.IsNullOrWhiteSpace(verb)) return false;
        var v = verb.Trim().ToLowerInvariant();
        return v is "git" or "git_scene"
            or "shell" or "shell_scene"
            or "browser" or "net" or "internet" or "internet_browser" or "scene_internet_browser"
            or "mcp" or "mcp_scene";
    }

    public static object Pane(string go, bool ok, string pulse) => new
    {
        ok,
        go,
        detail = "pulse",
        pulse,
        world = true,
        hint = "World channel: replace on M. pane_full= / go_detail=full for dump."
    };
}
