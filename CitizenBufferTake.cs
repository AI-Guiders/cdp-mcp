#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

internal static class CitizenBufferTake
{
    internal const int TakeShipMaxChars = 64_000;

    internal static CitizenIntentRouter.Route Route(string raw)
    {
        var path = CitizenIntentRouter.ExtractKeyedValue(raw, "path") ?? CitizenIntentRouter.ExtractPath(raw);
        if (!CitizenResultWake.TryPasteVerifyTakePath(path, out var verified, out var refuse))
        {
            return new CitizenIntentRouter.Route(
                CitizenIntentRouter.Verb.Take, raw, Ok: false, Op: "take", Path: verified, Reason: refuse, Go: "buffer");
        }
        path = verified;
        var anchor = CitizenIntentRouter.ExtractKeyedValue(raw, "anchor")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "at")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "from");
        var sniper = CitizenIntentRouter.ExtractKeyedValue(raw, "sniper");
        var useSniper = string.Equals(sniper, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sniper, "sniper", StringComparison.OrdinalIgnoreCase);
        return new CitizenIntentRouter.Route(
            CitizenIntentRouter.Verb.Take, raw, Ok: true, Op: "take",
            Path: string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
            Detail: string.IsNullOrWhiteSpace(anchor) ? null : anchor.Trim(),
            Scene: useSniper ? "sniper" : null, Go: "buffer");
    }

    internal static CitizenRouteHost.Applied Execute(
        CitizenIntentRouter.Route route,
        Func<IReadOnlyDictionary<string, JsonElement>, object>? callOverride)
    {
        const string op = "take";
        var args = BuildTakeArgs(route);
        try
        {
            object result;
            if (callOverride is { } ov) result = ov(args);
            else
            {
                var store = IdeLanguageTools.TryGetDocumentStore();
                if (store is null)
                    return new CitizenRouteHost.Applied(route.Raw, route.Verb.ToString(), Ok: false, Action: op, Path: route.Path, Reason: "doc_store_unbound");
                var session = CitizenRouteHost.SessionResolver?.Invoke();
                if (session is null)
                    return new CitizenRouteHost.Applied(route.Raw, route.Verb.ToString(), Ok: false, Action: op, Path: route.Path, Reason: "no_session");
                var byDomain = CitizenRouteHost.ByDomainResolver?.Invoke() ?? new Dictionary<string, ICdpBackendModule>(StringComparer.Ordinal);
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                result = DocumentEditPlane.DispatchAsync("cdp_buffer", store, session, byDomain, args, cts.Token)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
            }
            var json = result is string s ? s : JsonSerializer.Serialize(result);
            var ok = CitizenBufferComfort.TryReadUndoOk(json);
            var pulse = TryReadTakePulse(json);
            var ship = ok ? TryReadTakeShip(json) : null;
            if (ship is { Length: > 0 } && pulse is not null && pulse.IndexOf("ship=", StringComparison.Ordinal) < 0)
                pulse = CitizenRouteHost.TruncPulse(pulse + " ship=" + ship.Length);
            string? full = null; string? docId = null;
            CitizenEditResponse.TryReadEditMeta(json, out full, out docId);
            if (full is null) full = CitizenBufferComfort.TryReadRootPath(json);
            var seat = IdeDeskSeats.PlaceOrgan("editor_scene");
            return new CitizenRouteHost.Applied(route.Raw, route.Verb.ToString(), Ok: ok, Action: op, Seat: seat, Go: "editor_scene",
                Path: full ?? route.Path, DocId: docId, Pulse: pulse, Ship: ship,
                Reason: ok ? null : (CitizenRouteHost.TryReadLifecycleError(json) ?? CitizenBufferComfort.TryReadUndoError(json) ?? pulse ?? op + "_failed"));
        }
        catch (Exception ex)
        {
            return new CitizenRouteHost.Applied(route.Raw, route.Verb.ToString(), Ok: false, Action: op, Path: route.Path,
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildTakeArgs(CitizenIntentRouter.Route route)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement("take"),
            ["flush"] = JsonSerializer.SerializeToElement(true)
        };
        CitizenRouteHost.PutIfPresent(args, "path", route.Path ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "path"));
        CitizenRouteHost.PutIfPresent(args, "anchor",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "anchor")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "at")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "from")
            ?? route.Detail);
        CitizenRouteHost.PutIfPresent(args, "start_line", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "start_line"));
        CitizenRouteHost.PutIfPresent(args, "end_line", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "end_line"));
        CitizenRouteHost.PutIfPresent(args, "check", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "check"));
        CitizenRouteHost.PutIfPresent(args, "force", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "force"));
        CitizenRouteHost.PutIfPresent(args, "scope", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "scope"));
        CitizenRouteHost.PutIfPresent(args, "fence", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "fence"));
        CitizenRouteHost.PutIfPresent(args, "vision",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "vision")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "see"));
        var sniper = CitizenIntentRouter.ExtractKeyedValue(route.Raw, "sniper");
        if (string.Equals(sniper, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sniper, "sniper", StringComparison.OrdinalIgnoreCase)
            || string.Equals(route.Scene, "sniper", StringComparison.OrdinalIgnoreCase))
            args["sniper"] = JsonSerializer.SerializeToElement(true);
        return args;
    }

    static string? TryReadTakePulse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var bits = new List<string> { "take" };
            if (root.TryGetProperty("chars", out var c) && c.TryGetInt32(out var n))
                bits.Add("chars=" + n);
            else if (root.TryGetProperty("chars", out var cs) && cs.ValueKind == JsonValueKind.Number)
                bits.Add("chars=" + cs.GetRawText());
            if (root.TryGetProperty("lines", out var l) && l.TryGetInt32(out var lines))
                bits.Add("lines=" + lines);
            if (root.TryGetProperty("verify", out var v) && v.ValueKind == JsonValueKind.Object
                && v.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String && st.GetString() is { Length: > 0 } status)
                bits.Add(string.Equals(status, "skipped", StringComparison.OrdinalIgnoreCase) ? "verify=n/a" : status);
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String && err.GetString() is { Length: > 0 } error)
                bits.Add(error);
            return CitizenRouteHost.TruncPulse(string.Join(' ', bits));
        }
        catch { return CitizenRouteHost.TruncPulse("take"); }
    }

    static string? TryReadTakeShip(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string? ship = null;
            if (root.TryGetProperty("chat_markdown", out var md) && md.ValueKind == JsonValueKind.String)
                ship = md.GetString();
            if (string.IsNullOrWhiteSpace(ship) && root.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String)
                ship = body.GetString();
            if (string.IsNullOrWhiteSpace(ship)) return null;
            ship = ship.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
            return ship.Length <= TakeShipMaxChars ? ship : ship[..TakeShipMaxChars] + "\n…[ship truncated chars=" + ship.Length + "]";
        }
        catch { return null; }
    }
}
