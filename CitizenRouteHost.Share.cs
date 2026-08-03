#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent share — sync IdeShare via DocumentEditPlane (operator inbox / self shelf).</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake share JSON; live uses DocumentEditPlane.DispatchAsync.</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, object>? ShareCallOverride { get; set; }

    static Applied RunShare(CitizenIntentRouter.Route route)
    {
        const string op = "share";
        var args = BuildShareArgs(route);

        try
        {
            object result;
            if (ShareCallOverride is { } ov)
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
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
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
            var pulse = TryReadSharePulse(json);
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
                Reason: ok ? null : (TryReadLifecycleError(json) ?? TryReadUndoError(json) ?? pulse ?? op + "_failed"));
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

    static Dictionary<string, JsonElement> BuildShareArgs(CitizenIntentRouter.Route route)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement("share"),
            ["flush"] = JsonSerializer.SerializeToElement(true)
        };

        PutIfPresent(args, "path", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "path") ?? route.Path);
        PutIfPresent(args, "with",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "with")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "to"));
        PutIfPresent(args, "from", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "from"));
        PutIfPresent(args, "body",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "body")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "text")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "content")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "notes")
            ?? route.NewString);
        PutIfPresent(args, "ask",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "ask") ?? route.Scene);
        PutIfPresent(args, "dir",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "dir")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "inbox"));
        PutIfPresent(args, "anchor",
            CitizenIntentRouter.ExtractKeyedValue(route.Raw, "anchor")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "at"));
        PutIfPresent(args, "start_line", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "start_line"));
        PutIfPresent(args, "end_line", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "end_line"));
        PutIfPresent(args, "depth", CitizenIntentRouter.ExtractKeyedValue(route.Raw, "depth"));

        return args;
    }

    static string? TryReadSharePulse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var bits = new List<string> { "share" };
            if (root.TryGetProperty("with", out var w) && w.ValueKind == JsonValueKind.String
                && w.GetString() is { Length: > 0 } with)
                bits.Add(with);
            if (root.TryGetProperty("from", out var f) && f.ValueKind == JsonValueKind.String
                && f.GetString() is { Length: > 0 } from)
                bits.Add("from=" + from);
            if (root.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String
                && st.GetString() is { Length: > 0 } status)
                bits.Add(status);
            if (root.TryGetProperty("chars", out var c) && c.TryGetInt32(out var n))
                bits.Add("chars=" + n);
            else if (root.TryGetProperty("chars", out var cs) && cs.ValueKind == JsonValueKind.Number)
                bits.Add("chars=" + cs.GetRawText());
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 } error)
                bits.Add(error);
            return TruncPulse(string.Join(' ', bits));
        }
        catch
        {
            return TruncPulse("share");
        }
    }
}
