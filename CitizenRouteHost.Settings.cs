#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Citizen @intent settings|options — sync MetaDispatch cdp_settings; place settings organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake settings JSON; live uses MetaDispatchResolver("cdp_settings", …).</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, string>? SettingsDispatchOverride { get; set; }

    static Applied RunSettings(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "options" : route.Op!;
        var args = BuildSettingsArgs(route, op);

        try
        {
            string json;
            if (SettingsDispatchOverride is { } ov)
            {
                json = ov(args);
            }
            else
            {
                var meta = MetaDispatchResolver
                    ?? ((_, _, _) => Task.FromResult("""{"ok":false,"error":"meta_dispatch_unbound"}"""));
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                json = meta("cdp_settings", args, cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            var ok = TryReadLifecycleOk(json) || TryReadSettingsOk(json);
            var pulse = TryReadSettingsPulse(json, op, route.Path);
            var seat = IdeDeskSeats.PlaceOrgan("settings");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "settings",
                Seat: seat,
                Go: "settings",
                Path: route.Path,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "settings_failed"));
        }
        catch (OperationCanceledException)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "settings",
                Go: "settings",
                Path: route.Path,
                Reason: "settings_timeout");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "settings",
                Go: "settings",
                Path: route.Path,
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildSettingsArgs(
        CitizenIntentRouter.Route route, string op)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };
        var raw = route.Raw;

        if (op is "page" or "catalog")
        {
            PutIfPresent(args, "page", route.Path
                ?? route.Scene
                ?? CitizenIntentRouter.ExtractKeyedValue(raw, "page"));
            PutIfPresent(args, "section",
                CitizenIntentRouter.ExtractKeyedValue(raw, "section"));
            PutBoolIfPresent(args, "writable_only",
                CitizenIntentRouter.ExtractKeyedValue(raw, "writable_only"));
        }
        else if (op is "get" or "set" or "unset")
        {
            PutIfPresent(args, "key", route.Path
                ?? CitizenIntentRouter.ExtractKeyedValue(raw, "key"));
            if (op is "set")
                PutIfPresent(args, "value", route.Tool
                    ?? CitizenIntentRouter.ExtractKeyedValue(raw, "value"));
        }
        else if (op is "lsp_probe" or "lsp_install" or "lsp_ensure" or "lsp_add")
        {
            PutIfPresent(args, "id", route.Tool
                ?? CitizenIntentRouter.ExtractKeyedValue(raw, "id"));
            PutIfPresent(args, "language",
                CitizenIntentRouter.ExtractKeyedValue(raw, "language"));
            PutIfPresent(args, "via", route.Detail
                ?? CitizenIntentRouter.ExtractKeyedValue(raw, "via"));
            if (op is "lsp_add")
                PutIfPresent(args, "command", route.Command
                    ?? CitizenIntentRouter.ExtractKeyedValue(raw, "command"));
        }

        return args;
    }

    static bool TryReadSettingsOk(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var okEl))
                return okEl.ValueKind != JsonValueKind.False;
            if (root.TryGetProperty("error", out var err)
                && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 })
                return false;
            return root.TryGetProperty("op", out _) || root.TryGetProperty("schema", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadSettingsPulse(string json, string op, string? path)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String
                && p.GetString() is { Length: > 0 } pulse)
                return TruncPulse(pulse);

            var bits = new List<string> { "settings", op };
            if (root.TryGetProperty("ok", out var okEl))
                bits.Add(okEl.ValueKind == JsonValueKind.True ? "ok" : "fail");
            if (root.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String
                && title.GetString() is { Length: > 0 } t)
                bits.Add(t);
            else if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 } e)
                bits.Add(e);

            if (path is { Length: > 0 })
                bits.Add(path);

            return TruncPulse(string.Join(' ', bits));
        }
        catch
        {
            return TruncPulse("settings " + op);
        }
    }
}
