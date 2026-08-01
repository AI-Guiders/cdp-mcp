using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Pack dogfood plane (include_pack) for cdp_session.</summary>
internal static partial class SessionPlane
{
    private static async Task<PackPlaneResult> BuildPackPlaneAsync(
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        string packId,
        string processId,
        string? procedureId)
    {
        // worlds/ packs → memory_world; domains/ consumer (agent-operations-cdp) → memory_skill.
        // Try primary then alternate so pack_id dogfood does not die on wrong allowed_roots.
        var ordered = ResolvePackBackends(byDomain, packId);
        if (ordered.Count == 0)
            return PackPlaneResult.Unavailable("memory_world_and_skill_disabled");

        string facet = ordered[0].Facet;
        try
        {
            string? listRaw = null;
            string? usedFacet = null;
            ICdpBackendModule? packBackend = null;
            foreach (var (candidateFacet, backend) in ordered)
            {
                var tryList = await backend.CallAsync(
                    "list_pack",
                    new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                    {
                        ["pack_id"] = JsonSerializer.SerializeToElement(packId)
                    }).ConfigureAwait(false);
                if (!PackCallOk(tryList))
                    continue;
                listRaw = tryList;
                usedFacet = candidateFacet;
                packBackend = backend;
                facet = candidateFacet;
                break;
            }

            if (listRaw is null || packBackend is null || usedFacet is null)
                return PackPlaneResult.Failed(facet, packId, "pack_not_found_on_world_or_skill");

            var processArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["pack_id"] = JsonSerializer.SerializeToElement(packId),
                ["process_id"] = JsonSerializer.SerializeToElement(processId)
            };
            var processRaw = await packBackend.CallAsync("get_process", processArgs).ConfigureAwait(false);

            // debug-radius lives in epistemic-scene (worlds/); always pull via world when available.
            var radiusBackend = byDomain.TryGetValue(CdpDomains.MemoryWorld, out var worldRadius) && worldRadius.IsEnabled
                ? worldRadius
                : packBackend;
            var defRaw = await radiusBackend.CallAsync(
                "get_definition",
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["pack_id"] = JsonSerializer.SerializeToElement("epistemic-scene"),
                    ["definition_id"] = JsonSerializer.SerializeToElement("debug-radius")
                }).ConfigureAwait(false);

            JsonEmbed? procedureEmbed = null;
            if (!string.IsNullOrWhiteSpace(procedureId))
            {
                var procedureRaw = await packBackend.CallAsync(
                    "get_procedure",
                    new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                    {
                        ["pack_id"] = JsonSerializer.SerializeToElement(packId),
                        ["procedure_id"] = JsonSerializer.SerializeToElement(procedureId)
                    }).ConfigureAwait(false);
                using var procedureDoc = JsonDocument.Parse(procedureRaw);
                procedureEmbed = JsonEmbed.From(procedureDoc.RootElement, 3000);
            }

            using var listDoc = JsonDocument.Parse(listRaw);
            using var processDoc = JsonDocument.Parse(processRaw);
            using var defDoc = JsonDocument.Parse(defRaw);

            return new PackPlaneResult
            {
                Available = true,
                Facet = usedFacet,
                PackId = packId,
                ProcessId = processId,
                ProcedureId = procedureId,
                List = JsonEmbed.From(listDoc.RootElement, 2500),
                Process = JsonEmbed.From(processDoc.RootElement, 3500),
                Procedure = procedureEmbed,
                DefinitionDebugRadius = JsonEmbed.From(defDoc.RootElement, 2500),
                SuggestedNext = BuildPackSuggestedNext(usedFacet, packId)
            };
        }
        catch (Exception ex)
        {
            return PackPlaneResult.Failed(facet, packId, ex.Message);
        }
    }

    private static List<(string Facet, ICdpBackendModule Backend)> ResolvePackBackends(
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        string packId)
    {
        byDomain.TryGetValue(CdpDomains.MemoryWorld, out var world);
        byDomain.TryGetValue(CdpDomains.MemorySkill, out var skill);
        var consumerFirst = packId.Contains("agent-operations", StringComparison.OrdinalIgnoreCase);

        var list = new List<(string, ICdpBackendModule)>(2);
        void Add(string facet, ICdpBackendModule? mod)
        {
            if (mod is { IsEnabled: true })
                list.Add((facet, mod));
        }

        if (consumerFirst)
        {
            Add(CdpDomains.MemorySkill, skill);
            Add(CdpDomains.MemoryWorld, world);
        }
        else
        {
            Add(CdpDomains.MemoryWorld, world);
            Add(CdpDomains.MemorySkill, skill);
        }

        return list;
    }

    private static bool PackCallOk(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False)
                return false;
            if (doc.RootElement.TryGetProperty("error", out var err)
                && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 } e
                && e.Contains("pack_not_found", StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static SuggestedNextDto BuildPackSuggestedNext(string facet, string packId)
    {
        var consumer = packId.Contains("agent-operations", StringComparison.OrdinalIgnoreCase);
        var candidates = new List<SuggestedCandidateDto>
        {
            SuggestedCandidateDto.Tool($"{facet}_get_definition", definitionId: "debug-radius"),
            SuggestedCandidateDto.Tool($"{CdpDomains.MemoryWorld}_get_definition", definitionId: "kolb-journal"),
            SuggestedCandidateDto.Tool(
                $"{CdpDomains.MemoryWorld}_get_process",
                processId: "curiosity-kolb-loop",
                hint: "gap → spaces → (A)→(I) → journal settle"),
            SuggestedCandidateDto.Tool(
                $"{CdpDomains.MemoryWorld}_get_procedure",
                procedureId: "kolb-journal-park",
                hint: "when-card: append JOURNAL.jsonl"),
            SuggestedCandidateDto.Tool(
                $"{CdpDomains.MemoryWorld}_get_procedure",
                procedureId: "intake-brief-plan",
                hint: "when-card: what+why before explore"),
            SuggestedCandidateDto.Tool(
                $"{CdpDomains.MemoryWorld}_get_process",
                processId: "applicability-then-infer",
                hint: "major promote: (A) then (I)"),
            SuggestedCandidateDto.Tool($"{facet}_radius_gate_check", hint: "delta_radius < 0"),
        };

        if (consumer)
        {
            candidates.Insert(1, SuggestedCandidateDto.Tool(
                $"{facet}_get_definition",
                definitionId: "migrate-one-then-batch"));
            candidates.Insert(2, SuggestedCandidateDto.Tool(
                $"{facet}_get_procedure",
                procedureId: "migrate-one-then-batch",
                hint: "when-card: golden pair before batch"));
            candidates.Add(SuggestedCandidateDto.Cue(
                "Mass migrate — procedure migrate-one-then-batch before script-first?"));
        }

        candidates.Add(SuggestedCandidateDto.Cue(
            "Kolb park: reuse A&H drafts/INDEX — scout before invent; pin → JOURNAL.jsonl?"));
        candidates.Add(SuggestedCandidateDto.Cue(
            "Named what+why (intake-brief-plan) before explore — or exploring to stall?"));

        return new SuggestedNextDto
        {
            Policy = "ask",
            Note = "Agent Env: no CIDE enqueue — ask before promote; use memory_skill_radius_gate_check.",
            Candidates = candidates.ToArray()
        };
    }

    private static readonly IReadOnlyDictionary<string, JsonElement> FrozenEmpty =
        new Dictionary<string, JsonElement>();

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "\n…(truncated)";
}
