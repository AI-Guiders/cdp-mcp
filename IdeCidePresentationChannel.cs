#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// Desk tap for operator CIDE presentation topology (not agent desk Options).
/// <c>op=set topology=(P)(F)(M)</c> → presentation-LATEST latch → CIDE live reparse.
/// </summary>
internal static class IdeCidePresentationChannel
{
    public static string HandleJson(IReadOnlyDictionary<string, JsonElement> args)
    {
        var op = Arg(args, "op") ?? "scene";
        return op.Trim().ToLowerInvariant() switch
        {
            "scene" or "get" => Scene(),
            "set" => Set(args),
            _ => Fail("unknown_op", "op=scene|get|set topology=")
        };
    }

    static string Scene()
    {
        var latch = CidePresentationLatch.TryRead();
        return JsonSerializer.Serialize(new
        {
            schema = "cide_presentation/v0",
            ok = true,
            op = "scene",
            role = "cide_presentation",
            latch_path = CidePresentationLatch.LatchPath,
            topology = latch?.Topology,
            origin = latch?.Origin,
            stamped_utc = latch?.StampedUtc,
            hint = "set topology=(P)(F)(M) — publishes latch; CIDE projector applies live. Not agent cdp_settings desk.",
            next = new object[]
            {
                new { go = "cide_presentation_set", label = "Set topology", why = "topology=(P)(F)(M)" },
                new { go = "cide_presentation", label = "Scene", why = "op=scene" }
            }
        });
    }

    static string Set(IReadOnlyDictionary<string, JsonElement> args)
    {
        var topology = Arg(args, "topology") ?? Arg(args, "value") ?? Arg(args, "presentation");
        if (string.IsNullOrWhiteSpace(topology))
            return Fail("topology_required", "topology=(P)(F)(M) or (P+F)(M)");

        CidePresentationLatch.Publish(topology, CidePresentationLatch.OriginAgent);
        var latch = CidePresentationLatch.TryRead();
        return JsonSerializer.Serialize(new
        {
            schema = "cide_presentation/v0",
            ok = true,
            op = "set",
            topology = latch?.Topology ?? topology.Trim(),
            origin = CidePresentationLatch.OriginAgent,
            latch_path = CidePresentationLatch.LatchPath,
            stamped_utc = latch?.StampedUtc,
            hint = "Latch published — CIDE glass should reparse instantly when projector is up."
        });
    }

    static string? Arg(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
    }

    static string Fail(string reason, string authority) =>
        JsonSerializer.Serialize(new
        {
            schema = "cide_presentation/v0",
            ok = false,
            error = reason,
            detail = authority
        });
}
