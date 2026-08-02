#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent debug — sync wait DebugPlane.DispatchAsync (DAP/bp organ parity).</summary>
internal static partial class CitizenRouteHost
{
    internal static Func<IReadOnlyDictionary<string, ICdpBackendModule>>? ByDomainResolver { get; set; }

    /// <summary>Tests: inject fake debug JSON; live uses <see cref="DebugPlane.DispatchAsync"/>.</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, CancellationToken, Task<string>>? DebugDispatchOverride { get; set; }

    static Applied RunDebug(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "scene" : route.Op!;
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };

        PutDebugKeyed(args, route.Raw, "path");
        PutDebugKeyed(args, route.Raw, "file_path");
        PutDebugKeyed(args, route.Raw, "line");
        PutDebugKeyed(args, route.Raw, "condition");
        PutDebugKeyed(args, route.Raw, "workspace_path");
        PutDebugKeyed(args, route.Raw, "target_path");
        PutDebugKeyed(args, route.Raw, "project_path");
        PutDebugKeyed(args, route.Raw, "process_id");
        if (route.Path is { Length: > 0 } && !args.ContainsKey("path"))
            args["path"] = JsonSerializer.SerializeToElement(route.Path);

        if (ExtractMcpKeyed(route.Raw, "breakpoints") is { Length: > 0 } bpJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(bpJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    args["breakpoints"] = doc.RootElement.Clone();
            }
            catch
            {
                return new Applied(
                    route.Raw,
                    route.Verb.ToString(),
                    Ok: false,
                    Action: "debug",
                    Go: "debug",
                    Reason: "debug_breakpoints_json_invalid");
            }
        }

        var session = SessionResolver?.Invoke();
        if (session is null && DebugDispatchOverride is null)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "debug",
                Go: "debug",
                Reason: "no_session");
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            string json;
            if (DebugDispatchOverride is { } ov)
            {
                json = ov(args, cts.Token).ConfigureAwait(false).GetAwaiter().GetResult();
            }
            else
            {
                var byDomain = ByDomainResolver?.Invoke()
                    ?? new Dictionary<string, ICdpBackendModule>(StringComparer.Ordinal);
                json = DebugPlane.DispatchAsync(session!, byDomain, args, cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            var ok = TryReadLifecycleOk(json);
            var pulse = TryReadDebugPulse(json, op);
            var seat = IdeDeskSeats.PlaceOrgan("debug");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "debug",
                Seat: seat,
                Go: "debug",
                Path: route.Path,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "debug_failed"));
        }
        catch (OperationCanceledException)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "debug",
                Go: "debug",
                Reason: "debug_timeout");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "debug",
                Go: "debug",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static void PutDebugKeyed(Dictionary<string, JsonElement> args, string raw, string key)
    {
        var v = ExtractMcpKeyed(raw, key);
        if (string.IsNullOrWhiteSpace(v))
            return;

        if ((key is "line" or "process_id") && int.TryParse(v, out var n))
            args[key] = JsonSerializer.SerializeToElement(n);
        else
            args[key] = JsonSerializer.SerializeToElement(v);
    }

    static string? TryReadDebugPulse(string json, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String)
                return TruncPulse(p.GetString());

            var bits = new List<string> { "debug", op };
            if (root.TryGetProperty("ok", out var okEl))
                bits.Add(okEl.ValueKind == JsonValueKind.True ? "ok" : "fail");
            if (root.TryGetProperty("active_dap", out var dap) && dap.ValueKind == JsonValueKind.True)
                bits.Add("dap");
            if (root.TryGetProperty("breakpoints", out var bps) && bps.ValueKind == JsonValueKind.Array)
                bits.Add("bp=" + bps.GetArrayLength());
            if (root.TryGetProperty("schema", out var sch) && sch.ValueKind == JsonValueKind.String
                && sch.GetString() is { Length: > 0 } sid)
                bits.Add(sid);

            return TruncPulse(string.Join(' ', bits));
        }
        catch
        {
            return TruncPulse(json);
        }
    }
}
