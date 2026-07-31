#nullable enable

namespace CdpMcp;

/// <summary>
/// Efferent peel #10: map parsed <c>@intent</c> lines to organ routes (go=/drill/open).
/// Does not CallTool — host executes <see cref="Route"/>. Refuse W-spray as thrash string.
/// </summary>
internal static class CitizenIntentRouter
{
    public enum Verb
    {
        Go,
        Drill,
        PaneFull,
        Open,
        Detail,
        Refuse,
        Unknown
    }

    public sealed record Route(
        Verb Verb,
        string Raw,
        bool Ok,
        string? Go = null,
        string? Organ = null,
        string? Path = null,
        string? Detail = null,
        string? Scene = null,
        string? Reason = null);

    public static IReadOnlyList<Route> RouteAll(IEnumerable<CitizenWireParser.Message>? messages)
    {
        if (messages is null)
            return [];

        var list = new List<Route>();
        foreach (var m in messages)
        {
            if (m.Kind != CitizenWireParser.Kind.Intent)
                continue;
            list.Add(RouteOne(m.IntentText ?? ""));
        }

        return list;
    }

    public static Route RouteOne(string? intentText)
    {
        var raw = (intentText ?? "").Trim();
        if (raw.Length == 0)
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "empty_intent");

        if (LooksLikeWSpray(raw))
        {
            return new Route(
                Verb.Refuse,
                raw,
                Ok: false,
                Reason: "refuse_w_spray — seats_detail=full / catalog dump is thrash");
        }

        if (raw.StartsWith("go=", StringComparison.OrdinalIgnoreCase))
        {
            var go = raw["go=".Length..].Trim();
            return go.Length == 0
                ? new Route(Verb.Unknown, raw, Ok: false, Reason: "go_empty")
                : new Route(Verb.Go, raw, Ok: true, Go: go);
        }

        if (raw.StartsWith("drill ", StringComparison.OrdinalIgnoreCase))
        {
            var organ = raw["drill ".Length..].Trim();
            return organ.Length == 0
                ? new Route(Verb.Unknown, raw, Ok: false, Reason: "drill_empty")
                : new Route(Verb.Drill, raw, Ok: true, Organ: organ, Go: MapDrillGo(organ));
        }

        if (raw.StartsWith("pane_full=", StringComparison.OrdinalIgnoreCase))
        {
            var seat = raw["pane_full=".Length..].Trim();
            return seat.Length == 0
                ? new Route(Verb.Unknown, raw, Ok: false, Reason: "pane_full_empty")
                : new Route(Verb.PaneFull, raw, Ok: true, Organ: seat, Go: "cockpit");
        }

        if (raw.StartsWith("open ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("open path=", StringComparison.OrdinalIgnoreCase))
        {
            var path = ExtractPath(raw);
            return string.IsNullOrWhiteSpace(path)
                ? new Route(Verb.Unknown, raw, Ok: false, Reason: "open_path_empty")
                : new Route(Verb.Open, raw, Ok: true, Path: path, Go: "buffer");
        }

        if (TryKv(raw, out var detail, out var scene)
            && (!string.IsNullOrEmpty(detail) || !string.IsNullOrEmpty(scene)))
        {
            return new Route(
                Verb.Detail,
                raw,
                Ok: true,
                Detail: detail,
                Scene: scene,
                Go: scene);
        }

        return new Route(Verb.Unknown, raw, Ok: false, Reason: "unrecognized_intent");
    }

    static bool LooksLikeWSpray(string raw)
    {
        if (raw.Contains("seats_detail=full", StringComparison.OrdinalIgnoreCase))
            return true;
        if (raw.Contains("ListTools", StringComparison.OrdinalIgnoreCase))
            return true;
        return raw.Contains("W-spray", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("wspray", StringComparison.OrdinalIgnoreCase);
    }

    static string? MapDrillGo(string organ)
    {
        var o = organ.Trim().ToLowerInvariant();
        return o switch
        {
            "editor" or "forward" or "f" => "editor_scene",
            "plan" or "p" => "plan",
            "shell" or "m" => "shell_scene",
            "alert" or "sa" => "alert",
            "pressure" => "pressure",
            _ => o
        };
    }

    static string? ExtractPath(string raw)
    {
        const string pathEq = "path=";
        var idx = raw.IndexOf(pathEq, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
            return raw[(idx + pathEq.Length)..].Trim().Trim('"');

        if (raw.StartsWith("open ", StringComparison.OrdinalIgnoreCase))
            return raw["open ".Length..].Trim().Trim('"');

        return null;
    }

    static bool TryKv(string raw, out string? detail, out string? scene)
    {
        detail = null;
        scene = null;
        var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var p in parts)
        {
            var eq = p.IndexOf('=');
            if (eq <= 0)
                continue;
            var key = p[..eq];
            var val = p[(eq + 1)..];
            if (key.Equals("detail", StringComparison.OrdinalIgnoreCase))
                detail = val;
            else if (key.Equals("scene", StringComparison.OrdinalIgnoreCase))
                scene = val;
        }

        return detail is not null || scene is not null;
    }
}
