#nullable enable
using System.Text;
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent inventory — sync IdeInventoryChannel; place inventory organ.</summary>
internal static partial class CitizenRouteHost
{
    internal static Func<SessionContext, IReadOnlyDictionary<string, JsonElement>, object>? InventoryHandleOverride { get; set; }

    static Applied RunInventory(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "scene" : route.Op!;
        var session = SessionResolver?.Invoke();
        if (session is null && InventoryHandleOverride is null)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "inventory",
                Go: "inventory",
                Reason: "no_session");
        }

        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };

        try
        {
            object result = InventoryHandleOverride is { } ov
                ? ov(session ?? new SessionContext(), args)
                : IdeInventoryChannel.HandleJson(session!, args);
            var json = result is string s ? s : JsonSerializer.Serialize(result);
            var ok = !json.Contains("\"ok\": false", StringComparison.OrdinalIgnoreCase);
            var seat = IdeDeskSeats.PlaceOrgan("inventory");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "inventory",
                Seat: seat,
                Go: "inventory",
                Pulse: TryReadInventoryPulse(json, op),
                Reason: ok ? null : "inventory_failed");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "inventory",
                Go: "inventory",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>
    /// Observe feed for next afferent — real inventory pulse/gaps, not bare <c>inventory scene</c>.
    /// </summary>
        /// <summary>
    /// Observe feed for next afferent — real inventory pulse/gaps, not bare <c>inventory scene</c>.
    /// All gap ids (canon list ≤15) must survive TruncPulse/EventPulseMax — Sierra asks for full ×N.
    /// </summary>
    internal static string? TryReadInventoryPulse(string json, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(ContextJsonBody(json));
            var root = doc.RootElement;
            var sb = new StringBuilder();

            if (root.TryGetProperty("pulse", out var p)
                && p.ValueKind == JsonValueKind.String
                && p.GetString() is { Length: > 0 } pulse)
            {
                sb.Append(pulse.Trim());
            }
            else
            {
                sb.Append("inventory ").Append(op);
            }

            if (root.TryGetProperty("gaps", out var gaps) && gaps.ValueKind == JsonValueKind.Array)
            {
                var n = 0;
                foreach (var g in gaps.EnumerateArray())
                {
                    // Match batch_size_recommend ceiling — never silent-drop last gaps under ×9.
                    if (n >= 15)
                        break;
                    var id = g.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                        ? idEl.GetString()
                        : null;
                    var status = g.TryGetProperty("status", out var stEl) && stEl.ValueKind == JsonValueKind.String
                        ? stEl.GetString()
                        : null;
                    if (string.IsNullOrWhiteSpace(id))
                        continue;
                    sb.Append(n == 0 ? " · gaps " : " ");
                    sb.Append(id.Trim());
                    if (!string.IsNullOrWhiteSpace(status))
                        sb.Append(':').Append(status.Trim());
                    n++;
                }
            }

            return TruncPulse(sb.ToString(), InventoryObservePulseMax);
        }
        catch
        {
            return TruncPulse("inventory " + op, InventoryObservePulseMax);
        }
    }
}
