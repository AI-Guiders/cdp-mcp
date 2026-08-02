#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent find — sync IdeFindChannel (peer dig without Cursor Grep).</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject shaped find result; live uses IdeFindChannel.Handle.</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, object>? FindCallOverride { get; set; }

    static Applied RunFind(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "run" : route.Op!;
        var args = BuildFindArgs(route);

        try
        {
            object result;
            if (FindCallOverride is { } ov)
            {
                result = ov(args);
            }
            else
            {
                var store = IdeLanguageTools.TryGetDocumentStore();
                if (store is null)
                {
                    return new Applied(
                        route.Raw,
                        route.Verb.ToString(),
                        Ok: false,
                        Action: "find",
                        Go: "find_desk",
                        Reason: "doc_store_unbound");
                }

                var session = SessionResolver?.Invoke();
                if (session is null)
                {
                    return new Applied(
                        route.Raw,
                        route.Verb.ToString(),
                        Ok: false,
                        Action: "find",
                        Go: "find_desk",
                        Reason: "no_session");
                }

                result = IdeFindChannel.Handle(store, session, args);
            }

            var ok = TryReadOk(result);
            var pulse = TryReadPulse(result) ?? TryReadFindErrorPulse(result, op);
            var seat = IdeDeskSeats.PlaceOrgan("find_desk");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "find",
                Seat: seat,
                Go: "find_desk",
                Path: route.Path,
                Pulse: pulse,
                Reason: ok ? null : (TryReadError(result) ?? pulse ?? "find_failed"));
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "find",
                Go: "find_desk",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildFindArgs(CitizenIntentRouter.Route route)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(route.Op ?? "run")
        };

        foreach (var key in FindArgKeys)
        {
            var val = ExtractMcpKeyed(route.Raw, key);
            if (val is { Length: > 0 })
                args[key] = JsonSerializer.SerializeToElement(val);
        }

        if (route.NewString is { Length: > 0 } query
            && !args.ContainsKey("query")
            && !args.ContainsKey("q")
            && !args.ContainsKey("text")
            && !args.ContainsKey("pattern"))
        {
            args["query"] = JsonSerializer.SerializeToElement(query);
        }

        if (route.Path is { Length: > 0 } path && !args.ContainsKey("path"))
            args["path"] = JsonSerializer.SerializeToElement(path);

        if (ExtractMcpKeyed(route.Raw, "regex") is { Length: > 0 } regexRaw
            && bool.TryParse(regexRaw, out var regex))
            args["regex"] = JsonSerializer.SerializeToElement(regex);

        if (ExtractMcpKeyed(route.Raw, "ignore_case") is { Length: > 0 } icRaw
            && bool.TryParse(icRaw, out var ignoreCase))
            args["ignore_case"] = JsonSerializer.SerializeToElement(ignoreCase);

        if (ExtractMcpKeyed(route.Raw, "max") is { Length: > 0 } maxRaw
            && int.TryParse(maxRaw, out var max))
            args["max"] = JsonSerializer.SerializeToElement(max);

        return args;
    }

    static readonly string[] FindArgKeys =
    [
        "query", "q", "text", "pattern",
        "where", "scope", "shape", "what",
        "path", "search_in", "root", "glob"
    ];

    static string? TryReadFindErrorPulse(object result, string op)
    {
        var err = TryReadError(result);
        return err is { Length: > 0 } ? $"find · {op} · {err}" : null;
    }
}
