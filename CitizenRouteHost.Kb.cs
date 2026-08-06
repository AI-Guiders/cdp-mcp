#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent kb|memory_* — sync CallAsync on memory facets (agent-notes / findings / failures / task).</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake backend JSON; live uses <see cref="ByDomainResolver"/>.</summary>
    internal static Func<string, string, IReadOnlyDictionary<string, JsonElement>, Task<string>>? KbCallOverride { get; set; }

    static Applied RunKb(CitizenIntentRouter.Route route)
    {
        var tool = string.IsNullOrWhiteSpace(route.Op) ? "list_pack" : route.Op!;
        var facet = string.IsNullOrWhiteSpace(route.Server) ? CdpDomains.MemoryWorld : route.Server!;
        var args = BuildKbArgs(route.Raw, tool, facet);

        try
        {
            string json;
            if (KbCallOverride is { } ov)
            {
                json = ov(facet, tool, args).ConfigureAwait(false).GetAwaiter().GetResult();
            }
            else
            {
                var byDomain = ByDomainResolver?.Invoke()
                    ?? new Dictionary<string, ICdpBackendModule>(StringComparer.Ordinal);
                if (!byDomain.TryGetValue(facet, out var backend) || !backend.IsEnabled)
                {
                    return new Applied(
                        route.Raw,
                        route.Verb.ToString(),
                        Ok: false,
                        Action: "kb",
                        Go: "kb",
                        Reason: "kb_facet_disabled:" + facet);
                }

                IReadOnlyDictionary<string, JsonElement> callArgs = args;
                var session = SessionResolver?.Invoke();
                if (session is not null && MemorySessionDefaults.IsMemoryDomain(facet))
                    callArgs = MemorySessionDefaults.WithWorkspace(args, session);

                json = backend.CallAsync(tool, callArgs)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            var ok = TryReadLifecycleOk(json);
            var pulse = TryReadKbPulse(json, facet, tool);
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "kb",
                Go: "kb",
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "kb_failed"));
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "kb",
                Go: "kb",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildKbArgs(string raw, string tool, string facet)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var key in KbArgKeys)
        {
            var val = ExtractMcpKeyed(raw, key);
            if (val is { Length: > 0 })
                args[key] = JsonSerializer.SerializeToElement(val);
        }

        if (!args.ContainsKey("definition_id")
            && ExtractMcpKeyed(raw, "id") is { Length: > 0 } id
            && tool is "get_definition")
        {
            args["definition_id"] = JsonSerializer.SerializeToElement(id);
        }

        if (tool is "get_definition" && !args.ContainsKey("definition_id"))
            args["definition_id"] = JsonSerializer.SerializeToElement("debug-radius");

        if (IsNotesFacet(facet)
            && (tool is "get_definition" or "list_pack" or "get_process" or "get_procedure" or "radius_gate_check")
            && !args.ContainsKey("pack_id") && !args.ContainsKey("pack_path"))
        {
            args["pack_id"] = JsonSerializer.SerializeToElement(
                string.Equals(facet, CdpDomains.MemorySkill, StringComparison.Ordinal)
                    ? "agent-operations-cdp"
                    : "epistemic-scene");
        }

        return args;
    }

    static bool IsNotesFacet(string facet) =>
        // Project roots rarely share world pack ids — do not silent-inject epistemic-scene.
        facet is CdpDomains.MemoryWorld or CdpDomains.MemorySkill;

    static readonly string[] KbArgKeys =
    [
        "pack_id", "pack_path", "definition_id", "process_id", "procedure_id",
        "file_path", "subdir", "path", "claim", "delta_radius",
        "radius_before", "radius_after", "workspace_path", "query", "q",
        "task_id", "relative_path", "section_id", "content", "status",
        "limit", "title", "summary", "tool", "error_or_miss"
    ];

    static string? TryReadKbPulse(string json, string facet, string tool)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var bits = new List<string> { "kb", facet, tool };
            if (root.TryGetProperty("ok", out var okEl))
                bits.Add(okEl.ValueKind == JsonValueKind.True ? "ok" : "fail");
            if (root.TryGetProperty("definition_id", out var d) && d.ValueKind == JsonValueKind.String
                && d.GetString() is { Length: > 0 } def)
                bits.Add(def);
            if (root.TryGetProperty("pack_id", out var p) && p.ValueKind == JsonValueKind.String
                && p.GetString() is { Length: > 0 } pack)
                bits.Add(pack);
            if (root.TryGetProperty("count", out var c) && c.TryGetInt32(out var n))
                bits.Add(n + " hit(s)");
            if (root.TryGetProperty("pulse", out var pulseEl) && pulseEl.ValueKind == JsonValueKind.String
                && pulseEl.GetString() is { Length: > 0 } pulse)
                bits.Add(TruncPulse(pulse) ?? pulse);
            if (root.TryGetProperty("llm_cue", out var cue) && cue.ValueKind == JsonValueKind.String
                && cue.GetString() is { Length: > 0 } cueText)
                bits.Add(TruncPulse(cueText) ?? cueText);
            if (root.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String
                && title.GetString() is { Length: > 0 } t)
                bits.Add(TruncPulse(t) ?? t);
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 } e)
                bits.Add(TruncPulse(e) ?? e);
            return TruncPulse(string.Join(' ', bits));
        }
        catch
        {
            return TruncPulse("kb " + facet + " " + tool);
        }
    }
}
