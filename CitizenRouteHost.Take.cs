#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent take — sync wait on DocumentEditPlane TakeShip (async).</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake take JSON; live uses DocumentEditPlane.DispatchAsync.</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, object>? TakeCallOverride { get; set; }

    static Applied RunTake(CitizenIntentRouter.Route route)
    {
        const string op = "take";
        var args = BuildTakeArgs(route);

        try
        {
            object result;
            if (TakeCallOverride is { } ov)
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
                        Action: op,
                        Path: route.Path,
                        Reason: "doc_store_unbound");
                }

                var session = SessionResolver?.Invoke();
                if (session is null)
                {
                    return new Applied(
                        route.Raw,
                        route.Verb.ToString(),
                        Ok: false,
                        Action: op,
                        Path: route.Path,
                        Reason: "no_session");
                }

                var byDomain = ByDomainResolver?.Invoke()
                    ?? new Dictionary<string, ICdpBackendModule>(StringComparer.Ordinal);
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                result = DocumentEditPlane.DispatchAsync(
                        "cdp_buffer",
                        store,
                        session,
                        byDomain,
                        args,
                        cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            var json = result is string s ? s : JsonSerializer.Serialize(result);
            var ok = TryReadUndoOk(json);
            var pulse = TryReadTakePulse(json);
            var ship = ok ? TryReadTakeShip(json) : null;
            if (ship is { Length: > 0 }
                && pulse is not null
                && pulse.IndexOf("ship=", StringComparison.Ordinal) < 0)
                pulse = TruncPulse(pulse + " ship=" + ship.Length);
            string? full = null;
            string? docId = null;
            TryReadEditMeta(json, out full, out docId);
            if (full is null)
                full = TryReadRootPath(json);
            var seat = IdeDeskSeats.PlaceOrgan("editor_scene");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: op,
                Seat: seat,
                Go: "editor_scene",
                Path: full ?? route.Path,
                DocId: docId,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? TryReadUndoError(json) ?? pulse ?? op + "_failed"),
                Ship: ship);
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: op,
                Path: route.Path,
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

        // Prefer Route.Path (paste-verified in RouteTake) over raw path=.
        PutIfPresent(args, "path", route.Path
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "path"));
        PutIfPresent(args, "anchor",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "anchor")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "at")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "from")
            ?? route.Detail);
        PutIfPresent(args, "start_line", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "start_line"));
        PutIfPresent(args, "end_line", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "end_line"));
        PutIfPresent(args, "check", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "check"));
        PutIfPresent(args, "force", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "force"));
        PutIfPresent(args, "scope", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "scope"));
        PutIfPresent(args, "fence", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "fence"));
        PutIfPresent(args, "vision",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "vision")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "see"));

        var sniper = CitizenIntentRouter.ExtractKeyedValue(route.Raw, "sniper");
        if (string.Equals(sniper, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sniper, "sniper", StringComparison.OrdinalIgnoreCase)
            || string.Equals(route.Scene, "sniper", StringComparison.OrdinalIgnoreCase))
            args["sniper"] = JsonSerializer.SerializeToElement(true);

        return args;
    }

    /// <summary>
    /// SoftFL lived: bare <c>skipped</c> in pulse reads as "content missed".
    /// TakeShip uses verify.status=skipped for no_kind_checker (.md) — map to verify=n/a.
    /// </summary>
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
                && v.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String
                && st.GetString() is { Length: > 0 } status)
            {
                // skipped ≠ failed — no checker for this kind (markdown etc.).
                bits.Add(string.Equals(status, "skipped", StringComparison.OrdinalIgnoreCase)
                    ? "verify=n/a"
                    : status);
            }
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 } error)
                bits.Add(error);
            return TruncPulse(string.Join(' ', bits));
        }
        catch
        {
            return TruncPulse("take");
        }
    }

    /// <summary>
    /// TakeShip ships <c>chat_markdown</c> (prefer) or <c>body</c> into agent context.
    /// Cursor MCP pastes tool result; Citizen Completions needs Applied.Ship → @event peer.
    /// </summary>
    internal const int TakeShipMaxChars = 64_000;

    static string? TryReadTakeShip(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string? ship = null;
            if (root.TryGetProperty("chat_markdown", out var md) && md.ValueKind == JsonValueKind.String)
                ship = md.GetString();
            if (string.IsNullOrWhiteSpace(ship)
                && root.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String)
                ship = body.GetString();
            if (string.IsNullOrWhiteSpace(ship))
                return null;
            ship = ship.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
            if (ship.Length <= TakeShipMaxChars)
                return ship;
            return ship[..TakeShipMaxChars] + "\n…[ship truncated chars=" + ship.Length + "]";
        }
        catch
        {
            return null;
        }
    }
}
