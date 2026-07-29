#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// Desk tap for operator CIDE glass (not agent desk Options).
/// <c>op=set</c> topology / tier / instruments / mfd_page → presentation-LATEST latch → CIDE live apply.
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
            _ => Fail("unknown_op", "op=scene|get|set topology=|tier=|pfd_primary=|mfd_page=")
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
            tier = latch?.Tier,
            instruments = latch?.Instruments,
            mfd_page = latch?.MfdPage,
            origin = latch?.Origin,
            stamped_utc = latch?.StampedUtc,
            hint = "set topology=(P)(F)(M) | tier=cockpit|compact|auto | pfd_primary=workspace_map | mfd_primary=solution_explorer_tree | mfd_page=SolutionExplorer. Not agent cdp_settings; no repo workspace.toml.",
            next = new object[]
            {
                new { go = "cide_presentation_set", label = "Set topology", why = "topology=(P)(F)(M)" },
                new { go = "cide_presentation_set", label = "Map on P", why = "pfd_primary=workspace_map" },
                new { go = "cide_presentation_set", label = "SE on M", why = "mfd_primary=solution_explorer_tree mfd_page=SolutionExplorer" },
                new { go = "cide_presentation", label = "Scene", why = "op=scene" }
            }
        });
    }

    static string Set(IReadOnlyDictionary<string, JsonElement> args)
    {
        var topology = Arg(args, "topology") ?? Arg(args, "value") ?? Arg(args, "presentation");
        var tier = Arg(args, "tier");
        var mfdPage = Arg(args, "mfd_page") ?? Arg(args, "page");
        var instruments = CollectInstruments(args);

        var patch = new CidePresentationLatch.PresentationPatch
        {
            Topology = topology,
            Tier = tier,
            Instruments = instruments,
            MfdPage = mfdPage
        };

        if (!patch.HasAny)
            return Fail("patch_required", "topology= and/or tier= and/or pfd_primary=|mfd_primary= and/or mfd_page=");

        if (tier is { Length: > 0 }
            && !tier.Equals("auto", StringComparison.OrdinalIgnoreCase)
            && !tier.Equals("compact", StringComparison.OrdinalIgnoreCase)
            && !tier.Equals("cockpit", StringComparison.OrdinalIgnoreCase))
            return Fail("tier_invalid", "tier=auto|compact|cockpit");

        CidePresentationLatch.Publish(patch, CidePresentationLatch.OriginAgent);
        var latch = CidePresentationLatch.TryRead();
        return JsonSerializer.Serialize(new
        {
            schema = "cide_presentation/v0",
            ok = true,
            op = "set",
            topology = latch?.Topology,
            tier = latch?.Tier,
            instruments = latch?.Instruments,
            mfd_page = latch?.MfdPage,
            origin = CidePresentationLatch.OriginAgent,
            latch_path = CidePresentationLatch.LatchPath,
            stamped_utc = latch?.StampedUtc,
            hint = "Latch published — CIDE glass applies when projector is up."
        });
    }

    static Dictionary<string, string>? CollectInstruments(IReadOnlyDictionary<string, JsonElement> args)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Take(string key)
        {
            var v = Arg(args, key);
            if (!string.IsNullOrWhiteSpace(v))
                map[key] = v.Trim();
        }

        Take("pfd_primary");
        Take("mfd_primary");
        Take("pfd_status_strip");
        Take("forward_status_strip");

        if (args.TryGetValue("instruments", out var el))
        {
            if (el.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in el.EnumerateObject())
                {
                    if (p.Value.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(p.Value.GetString()))
                        map[p.Name] = p.Value.GetString()!.Trim();
                }
            }
            else if (el.ValueKind == JsonValueKind.String)
            {
                var raw = el.GetString();
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(raw);
                        if (doc.RootElement.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var p in doc.RootElement.EnumerateObject())
                            {
                                if (p.Value.ValueKind == JsonValueKind.String
                                    && !string.IsNullOrWhiteSpace(p.Value.GetString()))
                                    map[p.Name] = p.Value.GetString()!.Trim();
                            }
                        }
                    }
                    catch
                    {
                        /* ignore bad instruments json */
                    }
                }
            }
        }

        return map.Count == 0 ? null : map;
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
