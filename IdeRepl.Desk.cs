#nullable enable
using System.Text;
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeRepl
{
    /// <summary>CCL verbs peeled from Apply (soft-warn). null = not handled.</summary>
    static (Dictionary<string, JsonElement> Args, object? Direct)? TryDesk(
        string head,
        IReadOnlyList<string> tokens,
        Dictionary<string, JsonElement> merged)
    {
        if (head is "help" or "?" or "h" or "ccc")
            return (merged, Help(null));

        if (head is "clear" or "seat_clear" or "reset")
        {
            merged["pin_clear"] = JsonSerializer.SerializeToElement(true);
            return (merged, null);
        }

        if (head is "layout" or "preset" or "desk")
        {
            if (tokens.Count < 2)
                return (merged, Err("layout needs id", "layout agent | layout cockpit | layout code+net"));
            merged["layout"] = JsonSerializer.SerializeToElement(tokens[1]);
            return (merged, null);
        }

        if (head is "seat")
        {
            if (tokens.Count < 3)
                return (merged, Err("seat needs seat + organ", "seat m git | seat forward editor"));
            merged["seat"] = JsonSerializer.SerializeToElement(tokens[1]);
            merged["organ"] = JsonSerializer.SerializeToElement(tokens[2]);
            return (merged, null);
        }

        if (head is "go" or "do" or "open")
        {
            if (tokens.Count < 2)
                return (merged, Err("go needs organ", "go browser | go editor | go report | go plan"));
            ApplyGo(merged, tokens, start: 1);
            return (merged, null);
        }

        if (head is "mfd" or "page")
        {
            if (tokens.Count < 2)
                return (merged, Err("mfd needs alias", "mfd nav | mfd chk → prefer go=sys|chk"));
            merged["mfd"] = JsonSerializer.SerializeToElement(tokens[1]);
            return (merged, null);
        }

        // Probe channel → script organ (ADR 0193).
        if (head is "probe" or "script")
        {
            if (tokens.Count >= 2)
            {
                var sub = tokens[1].ToLowerInvariant();
                if (sub is "check" or "compile")
                {
                    merged["go"] = JsonSerializer.SerializeToElement("script_check");
                    if (tokens.Count >= 3)
                        ApplyGoArgsOnly(merged, tokens, start: 2);
                    return (merged, null);
                }

                if (sub is "run" or "dry_run" or "dryrun")
                {
                    merged["go"] = JsonSerializer.SerializeToElement("script_run");
                    if (tokens.Count >= 3)
                        ApplyGoArgsOnly(merged, tokens, start: 2);
                    return (merged, null);
                }

                if (sub is "last" or "report")
                {
                    merged["go"] = JsonSerializer.SerializeToElement("report");
                    return (merged, null);
                }

                if (sub is "open" or "put" or "new")
                {
                    merged["go"] = JsonSerializer.SerializeToElement(sub is "open" ? "script_open" : "script_put");
                    if (tokens.Count >= 3)
                        ApplyGoArgsOnly(merged, tokens, start: 2);
                    return (merged, null);
                }

                // probe <path> → open
                merged["go"] = JsonSerializer.SerializeToElement("script_open");
                ApplyGoArgsOnly(merged, tokens, start: 1);
                return (merged, null);
            }

            merged["go"] = JsonSerializer.SerializeToElement("script_scene");
            return (merged, null);
        }

        if (head is "check" or "compile")
        {
            merged["go"] = JsonSerializer.SerializeToElement("script_check");
            if (tokens.Count >= 2)
                ApplyGoArgsOnly(merged, tokens, start: 1);
            return (merged, null);
        }

        if (head is "run")
        {
            merged["go"] = JsonSerializer.SerializeToElement("script_run");
            if (tokens.Count >= 2)
                ApplyGoArgsOnly(merged, tokens, start: 1);
            return (merged, null);
        }

        if (head is "report" or "evidence" or "pfd")
        {
            merged["go"] = JsonSerializer.SerializeToElement("report");
            return (merged, null);
        }

        if (head is "alert" or "eicas" or "sa")
        {
            merged["go"] = JsonSerializer.SerializeToElement("alert");
            return (merged, null);
        }

        if (head is "problems" or "problem" or "errlist" or "errorlist" or "err" or "diags")
        {
            merged["go"] = JsonSerializer.SerializeToElement("problems");
            if (tokens.Count >= 2)
            {
                var pick = tokens[1];
                merged["go_args"] = JsonSerializer.SerializeToElement(new { row = pick, aim = true });
            }
            return (merged, null);
        }

        return null;
    }
}
