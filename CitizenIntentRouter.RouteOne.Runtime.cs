#nullable enable

namespace CdpMcp;

/// <summary>RouteOne family gate: Runtime — peel method_lines off RouteOne.</summary>
internal static partial class CitizenIntentRouter
{
    static Route? TryRouteRuntime(string raw)
    {
        if (raw.Equals("test", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("test ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("test path=", StringComparison.OrdinalIgnoreCase))
        {
            var path = ExtractLifecyclePath(raw, "test");
            return new Route(Verb.Test, raw, Ok: true, Path: path, Go: "test");
        }

        if (raw.Equals("run", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("run ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("run path=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("dotnet_run", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("dotnet_run ", StringComparison.OrdinalIgnoreCase))
        {
            var path = ExtractLifecyclePath(
                raw.StartsWith("dotnet_run", StringComparison.OrdinalIgnoreCase)
                    ? "run" + raw["dotnet_run".Length..]
                    : raw,
                "run");
            return new Route(Verb.Run, raw, Ok: true, Path: path, Go: "run");
        }

        if (raw.Equals("mcp", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("mcp ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteMcp(raw);
        }

        if (raw.Equals("kb", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("kb ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("kb tool=", StringComparison.OrdinalIgnoreCase))
        {
            return RouteKb(raw);
        }

        if (raw.Equals("hci", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("hci ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("hci tool=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("codebase_index", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("codebase_index ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("hybrid_index", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("hybrid_index ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_hci", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_hci ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteHci(raw);
        }

        if (raw.Equals("shell", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("shell ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("shell command=", StringComparison.OrdinalIgnoreCase))
        {
            return RouteShell(raw);
        }

        if (raw.Equals("debug", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("debug ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("debug op=", StringComparison.OrdinalIgnoreCase))
        {
            return RouteDebug(raw);
        }

        if (raw.Equals("git", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("git ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("git tool=", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("git op=", StringComparison.OrdinalIgnoreCase))
        {
            return RouteGit(raw);
        }

        if (raw.Equals("ignite", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("ignite ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("ignite op=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("autoi", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("autoi ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteIgnite(raw);
        }

        if (raw.Equals("browser", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("browser ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("browser op=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("internet_browser", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("internet_browser ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("web", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("web ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("lynx", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("lynx ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteBrowser(raw);
        }

        if (raw.Equals("script", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("script ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("script op=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("csx", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("csx ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("script_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("script_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("script_put", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("script_put ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("script_open", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("script_open ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("script_check", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("script_check ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("script_run", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("script_run ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("script_last", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("script_last ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("script_help", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("script_help ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("script_report", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("script_report ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteScript(raw);
        }

        if (raw.Equals("ps1", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("ps1 ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("ise", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("ise ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("ps1_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("ps1_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("ps1_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("ps1_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_ps1", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_ps1 ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_ps1_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_ps1_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("ps1_put", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("ps1_put ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("ps1_open", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("ps1_open ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("ps1_check", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("ps1_check ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("ps1_run", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("ps1_run ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("ps1_last", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("ps1_last ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("ps1_help", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("ps1_help ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_ps1_put", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_ps1_put ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_ps1_open", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_ps1_open ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_ps1_check", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_ps1_check ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_ps1_run", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_ps1_run ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_ps1_last", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_ps1_last ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_ps1_help", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_ps1_help ", StringComparison.OrdinalIgnoreCase))
        {
            return RoutePs1(raw);
        }

        return null;
    }
}
