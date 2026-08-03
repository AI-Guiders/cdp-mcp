#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent cockpit|cdp_cockpit — agent desk pulse without Cursor MCP. go=cockpit stays Verb.Go place-only; cockpit_host stays Glass host.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteCockpit(string raw)
    {
        var work = NormalizeCockpitCompound(raw);
        var layout = ExtractKeyedValue(work, "layout");
        var paneFull = ExtractKeyedValue(work, "pane_full")
            ?? ExtractKeyedValue(work, "full_pane");
        var goDetail = ExtractKeyedValue(work, "go_detail");
        var deskDetail = ExtractKeyedValue(work, "desk_detail")
            ?? ExtractKeyedValue(work, "nav_detail");
        var locus = ExtractKeyedValue(work, "locus")
            ?? ExtractKeyedValue(work, "focus");

        return new Route(
            Verb.Cockpit,
            raw,
            Ok: true,
            Scene: string.IsNullOrWhiteSpace(layout) ? null : layout.Trim(),
            Organ: string.IsNullOrWhiteSpace(paneFull) ? null : paneFull.Trim(),
            Detail: string.IsNullOrWhiteSpace(goDetail) ? null : goDetail.Trim(),
            Tool: string.IsNullOrWhiteSpace(deskDetail) ? null : deskDetail.Trim(),
            Cmd: string.IsNullOrWhiteSpace(locus) ? null : locus.Trim(),
            Go: "cockpit");
    }

    static string NormalizeCockpitCompound(string raw)
    {
        foreach (var prefix in CockpitPrefixes)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "cockpit";
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;
            return "cockpit " + raw[prefix.Length..].TrimStart();
        }

        return raw;
    }

    static readonly string[] CockpitPrefixes =
    [
        "cdp_cockpit",
        "cockpit_desk",
        "agent_desk",
        "cockpit"
    ];
}
