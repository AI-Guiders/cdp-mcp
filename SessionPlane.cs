using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Agent-IDE session plane helpers (pillars 1 + 6 + pack dogfood).</summary>
internal static class SessionPlane
{
    private static readonly ContinuityDto DefaultContinuity = new()
    {
        DefaultMove =
            "Before deep topic: memory_session_route_context and/or scratch checkpoint-*.handoff.md; jsonl only if readable handoff has a hole.",
        AgentIdeCanon =
            "knowledge/work/projects/door-to-singularity/cascade-ide/note-cdp-agent-ide-six-pillars-v0.md",
        AgentEnv =
            "Agent Env first (CDP packs + radius gate); CIDE projector later. Self-continuation policy=ask until host enqueue exists."
    };

    public static ExplainToolResult ExplainTool(
        string toolName,
        SessionContext session,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        IReadOnlyList<ToolAffordance> allAffordances)
    {
        toolName = toolName.Trim();
        if (toolName.Length == 0)
            return new ExplainToolResult { Ok = false, Reason = "empty_tool_name" };

        if (toolName.StartsWith("cdp_", StringComparison.Ordinal))
            return new ExplainToolResult
            {
                Ok = true,
                Tool = toolName,
                Visibility = "meta",
                Detail = "Meta tools are always in ListTools."
            };

        if (IdeLanguageTools.IsBareVerb(toolName))
            return new ExplainToolResult
            {
                Ok = true,
                Tool = toolName,
                Visibility = "bare_ide",
                Detail = "Bare IDE verbs are always in ListTools; harness routes by session.language."
            };

        if (!CdpDomains.TrySplit(toolName, out var domain, out var underlying))
            return new ExplainToolResult
            {
                Ok = false,
                Tool = toolName,
                Reason = "unparseable_prefix",
                Detail = "Expected memory_*|debug_|build_ longest-prefix domain."
            };

        if (!byDomain.TryGetValue(domain, out var mod) || !mod.IsEnabled)
            return new ExplainToolResult
            {
                Ok = false,
                Tool = toolName,
                Domain = domain,
                Underlying = underlying,
                Reason = "domain_not_mounted",
                Detail = $"Backend '{domain}' disabled or missing in cdp-mcp.toml."
            };

        var affordance = allAffordances.FirstOrDefault(a =>
            a.Domain == domain &&
            (string.Equals(a.PrefixedName, toolName, StringComparison.Ordinal) ||
             string.Equals(a.UnderlyingName, underlying, StringComparison.Ordinal)));

        if (affordance is null)
            return new ExplainToolResult
            {
                Ok = false,
                Tool = toolName,
                Domain = domain,
                Underlying = underlying,
                Reason = "not_in_affordance_seed",
                Detail = "Mounted domain but tool not in Wave1 seed / catalog."
            };

        var hits = PhaseObjectCatalog.Query(
            allAffordances, session.Phase, session.Object, session.Intent, limit: 200, language: session.Language);
        var inShortlist = hits.Any(h =>
            string.Equals(h.Affordance.PrefixedName, affordance.PrefixedName, StringComparison.Ordinal));

        if (inShortlist)
            return new ExplainToolResult
            {
                Ok = true,
                Tool = affordance.PrefixedName,
                Domain = domain,
                Underlying = underlying,
                Visibility = "shortlist",
                Detail = "Visible for current cdp_context."
            };

        return new ExplainToolResult
        {
            Ok = false,
            Tool = affordance.PrefixedName,
            Domain = domain,
            Underlying = underlying,
            Reason = "filtered_by_catalog",
            Visibility = "hidden",
            Detail = "In seed but filtered by phase/object/language. Call cdp_context to retarget, or cdp_tools to preview.",
            Context = SessionContextWire.From(session),
            AffordancePhases = affordance.Phases.Select(CdpEnumParse.ToWire).ToArray(),
            AffordanceObjects = affordance.Objects.Select(CdpEnumParse.ToWire).ToArray(),
            AffordanceLanguages = affordance.EffectiveLanguages.ToArray()
        };
    }

    public static async Task<SessionPlaneResult> BuildSessionAsync(
        SessionContext session,
        IReadOnlyList<ICdpBackendModule> modules,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        IReadOnlyList<ToolAffordance> allAffordances,
        IReadOnlyDictionary<string, JsonElement> callArgs,
        int shortlistLimit = 12,
        WorkspacePlaneDto? workspace = null)
    {
        var hits = PhaseObjectCatalog.Query(
            allAffordances, session.Phase, session.Object, session.Intent, shortlistLimit, session.Language);

        ExplainToolResult? explain = null;
        if (callArgs.TryGetValue("explain_tool", out var et) && et.GetString() is { Length: > 0 } name)
            explain = ExplainTool(name, session, byDomain, allAffordances);

        var includeDebug = true;
        if (callArgs.TryGetValue("include_debug", out var id) && id.ValueKind is JsonValueKind.False)
            includeDebug = false;

        DebugStopDto debugStop;
        if (includeDebug && byDomain.TryGetValue(CdpDomains.Debug, out var dbg) && dbg.IsEnabled)
        {
            try
            {
                var raw = await dbg.CallAsync("debug_stop_context", FrozenEmpty).ConfigureAwait(false);
                debugStop = new DebugStopDto
                {
                    Available = true,
                    StopContext = Truncate(raw, 4000)
                };
            }
            catch (Exception ex)
            {
                debugStop = new DebugStopDto { Available = true, Error = ex.Message };
            }
        }
        else
        {
            debugStop = new DebugStopDto { Available = false };
        }

        var includePack = true;
        if (callArgs.TryGetValue("include_pack", out var ip) && ip.ValueKind is JsonValueKind.False)
            includePack = false;

        var packId = "epistemic-scene";
        if (callArgs.TryGetValue("pack_id", out var pid) && pid.GetString() is { Length: > 0 } p)
            packId = p.Trim();

        var processId = "bug-radius-shrink";
        if (callArgs.TryGetValue("process_id", out var prid) && prid.GetString() is { Length: > 0 } pr)
            processId = pr.Trim();

        string? procedureId = null;
        if (callArgs.TryGetValue("procedure_id", out var procEl) && procEl.GetString() is { Length: > 0 } proc)
            procedureId = proc.Trim();
        else if (string.Equals(processId, "curiosity-kolb-loop", StringComparison.OrdinalIgnoreCase))
            procedureId = "kolb-journal-park";

        PackPlaneResult? packPlane = null;
        if (includePack)
            packPlane = await BuildPackPlaneAsync(byDomain, packId, processId, procedureId).ConfigureAwait(false);

        return new SessionPlaneResult
        {
            Context = SessionContextWire.From(session),
            Workspace = workspace,
            Shortlist = hits.Select(h => new ShortlistItemDto
            {
                Name = h.Affordance.PrefixedName,
                Score = h.Score,
                Hint = h.Affordance.Hint
            }).ToArray(),
            Health = new SessionHealthDto
            {
                Backends = modules.Select(m => new BackendHealthDto
                {
                    Domain = m.Domain,
                    Enabled = m.IsEnabled,
                    Health = m.HealthSummary
                }).ToArray()
            },
            Debug = debugStop,
            Pack = packPlane,
            Continuity = DefaultContinuity,
            ExplainTool = explain
        };
    }

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
