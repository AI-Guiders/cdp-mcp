#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Citizen @intent mcp — sync wait McpOutletHabitat.DispatchAsync (facade organ parity).</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake dispatch JSON; live uses <see cref="McpOutletHabitat.Instance"/>.</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, CancellationToken, Task<string>>? McpDispatchOverride { get; set; }

    static Applied RunMcp(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "scene" : route.Op!;
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };
        if (route.Server is { Length: > 0 } server)
            args["server"] = JsonSerializer.SerializeToElement(server);
        if (route.Tool is { Length: > 0 } tool)
            args["tool"] = JsonSerializer.SerializeToElement(tool);
        if (route.Preset is { Length: > 0 } preset)
            args["preset"] = JsonSerializer.SerializeToElement(preset);
        if (ExtractMcpKeyed(route.Raw, "command") is { Length: > 0 } command)
            args["command"] = JsonSerializer.SerializeToElement(command);
        if (ExtractMcpKeyed(route.Raw, "filter") is { Length: > 0 } filter)
            args["filter"] = JsonSerializer.SerializeToElement(filter);
        if (ExtractMcpKeyed(route.Raw, "args") is { Length: > 0 } argsJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(argsJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    args["args"] = doc.RootElement.Clone();
            }
            catch
            {
                return new Applied(
                    route.Raw,
                    route.Verb.ToString(),
                    Ok: false,
                    Action: "mcp",
                    Go: "mcp",
                    Reason: "mcp_args_json_invalid");
            }
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            string json;
            if (McpDispatchOverride is { } ov)
            {
                json = ov(args, cts.Token).ConfigureAwait(false).GetAwaiter().GetResult();
            }
            else
            {
                var outlet = McpOutletHabitat.Instance;
                if (outlet is null)
                {
                    return new Applied(
                        route.Raw,
                        route.Verb.ToString(),
                        Ok: false,
                        Action: "mcp",
                        Go: "mcp",
                        Reason: "no_outlet");
                }

                json = outlet.DispatchAsync(args, cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            var ok = TryReadLifecycleOk(json);
            var pulse = TryReadMcpPulse(json, op);
            var seat = IdeDeskSeats.PlaceOrgan("mcp");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "mcp",
                Seat: seat,
                Go: "mcp",
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "mcp_failed"));
        }
        catch (OperationCanceledException)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "mcp",
                Go: "mcp",
                Reason: "mcp_timeout");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "mcp",
                Go: "mcp",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static string? ExtractMcpKeyed(string raw, string key)
    {
        var needle = key + "=";
        var idx = raw.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;

        var i = idx + needle.Length;
        if (i >= raw.Length)
            return "";

        if (raw[i] == '"')
        {
            var end = raw.IndexOf('"', i + 1);
            if (end < 0)
                return raw[(i + 1)..];
            return raw[(i + 1)..end];
        }

        var rest = raw[i..];
        var sp = rest.IndexOf(' ');
        return sp < 0 ? rest : rest[..sp];
    }

    static string? TryReadMcpPulse(string json, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String)
                return TruncPulse(p.GetString());

            var bits = new List<string> { "mcp", op };
            if (root.TryGetProperty("ok", out var okEl))
                bits.Add(okEl.ValueKind == JsonValueKind.True ? "ok" : "fail");
            if (root.TryGetProperty("server", out var s) && s.ValueKind == JsonValueKind.String
                && s.GetString() is { Length: > 0 } sid)
                bits.Add(sid);
            if (root.TryGetProperty("tool", out var t) && t.ValueKind == JsonValueKind.String
                && t.GetString() is { Length: > 0 } tid)
                bits.Add(tid);
            if (root.TryGetProperty("count", out var c) && c.TryGetInt32(out var n))
                bits.Add("n=" + n);
            if (root.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String
                && text.GetString() is { Length: > 0 } body)
                bits.Add(TruncPulse(body) ?? body);

            return TruncPulse(string.Join(' ', bits));
        }
        catch
        {
            return TruncPulse(json);
        }
    }
}
