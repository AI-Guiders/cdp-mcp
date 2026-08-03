#nullable enable
using System.Text.Json;

namespace CdpMcp;
internal static partial class CitizenRouteHost
{
    static Applied RunPlanCmd(CitizenIntentRouter.Route route)
    {
        var cmd = route.Cmd?.Trim() ?? "";
        if (cmd.Length == 0)
        {
            return new Applied(route.Raw, route.Verb.ToString(), Ok: false, Action: "repl", Reason: "cmd_empty");
        }

        try
        {
            var applied = IdeRepl.Apply(cmd, new Dictionary<string, JsonElement>(StringComparer.Ordinal));
            if (applied is null)
            {
                return new Applied(route.Raw, route.Verb.ToString(), Ok: false, Action: "repl", Cmd: cmd, Reason: "repl_null");
            }

            var (args, direct) = applied.Value;
            if (direct is not null)
            {
                var err = TryReadCclError(direct);
                return new Applied(route.Raw, route.Verb.ToString(), Ok: false, Action: "repl", Cmd: cmd, Go: "plan", Reason: err ?? "ccl_direct");
            }

            if (!args.TryGetValue("tm_op", out var tmEl) || tmEl.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(tmEl.GetString()))
            {
                if (args.TryGetValue("go", out var goEl) && goEl.ValueKind == JsonValueKind.String && goEl.GetString() is { Length: > 0 } goOnly)
                {
                    var placedOnly = IdeDeskSeats.PlaceOrgan(goOnly);
                    return new Applied(route.Raw, route.Verb.ToString(), Ok: placedOnly is not null, Action: "repl_place", Seat: placedOnly, Go: IdeDeskSeats.CanonicalOrganPin(goOnly), Cmd: cmd, Reason: placedOnly is null ? "place_failed" : null);
                }

                return new Applied(route.Raw, route.Verb.ToString(), Ok: false, Action: "repl", Cmd: cmd, Reason: "no_tm_op");
            }

            if (!IdeStageCycle.TryWorkspace(out var store, out var state, out var phase))
            {
                return new Applied(route.Raw, route.Verb.ToString(), Ok: false, Action: "repl", Cmd: cmd, Go: "plan", Reason: "no_workspace");
            }

            var tmArgs = new Dictionary<string, JsonElement>(args, StringComparer.Ordinal);
            if (phase is { Length: > 0 })
                tmArgs["session_phase"] = JsonSerializer.SerializeToElement(phase);
            var root = IdeCockpitHostChannel.ProjectRootResolver?.Invoke();
            if (root is { Length: > 0 })
                tmArgs["project_root"] = JsonSerializer.SerializeToElement(root);
            var result = IdeTaskManager.Handle(store, state, tmArgs);
            var pulse = TryReadPulse(result);
            var ok = TryReadOk(result);
            var seat = IdeDeskSeats.PlaceOrgan("plan");
            return new Applied(route.Raw, route.Verb.ToString(), Ok: ok, Action: "repl", Seat: seat, Go: "plan", Cmd: cmd, Pulse: pulse, Reason: ok ? null : (TryReadError(result) ?? pulse ?? "tm_failed"));
        }
        catch (Exception ex)
        {
            return new Applied(route.Raw, route.Verb.ToString(), Ok: false, Action: "repl", Cmd: cmd, Go: "plan", Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static string? TryReadCclError(object direct)
    {
        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(direct));
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String)
            {
                var err = e.GetString();
                if (root.TryGetProperty("hint", out var h) && h.ValueKind == JsonValueKind.String)
                    return err + " · " + h.GetString();
                return err;
            }
        }
        catch
        {
        /* best-effort */
        }

        return null;
    }

    static string? TryReadPulse(object result)
    {
        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            if (doc.RootElement.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String)
                return p.GetString();
        }
        catch
        {
        /* best-effort */
        }

        return null;
    }

    static string? TryReadError(object result)
    {
        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            if (doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String && e.GetString()is { Length: > 0 } err)
                return err.Trim();
        }
        catch
        {
        /* best-effort */
        }

        return null;
    }

    static bool TryReadOk(object result)
    {
        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            if (doc.RootElement.TryGetProperty("ok", out var o) && o.ValueKind is JsonValueKind.True or JsonValueKind.False)
                return o.GetBoolean();
        }
        catch
        {
        /* assume ok if unreadable */
        }

        return true;
    }
}