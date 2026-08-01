using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Agent-IDE session plane helpers (pillars 1 + 6 + pack dogfood).</summary>
internal static partial class SessionPlane
{
    public static readonly ContextBudgetDto DefaultContextBudget = new()
    {
        Warning =
            "seats_detail=full spray · ListTools/schemas thrash · " +
            "include_pack=true when A session would do · unread shell_history walls",
        Caution =
            "go_detail=full · pane_full= · desk_detail=nav/full · shell_last tails · " +
            "buffer take/read whole files · include_pack=true — opt-in when you need the dump",
        Advisory =
            "cdp_cockpit slim/pulse · cdp_session (pack omitted) · cdp_context · cdp_tools · cdp_health · " +
            "git/shell pulse · buffer scene|diagnostics — Dark Cockpit default",
        Habit =
            "Stay A; escalate one C; never spray W. Tags on tool Meta: [A]/[C]/[W]. man tool=context_budget."
    };

    /// <summary>Shared W/C/A cheat sheet (continuity + cdp_man tool=context_budget).</summary>
    public static string ContextBudgetManual =>
        "Context budget — EICAS W/C/A (CIDE ADR 0021; not TCAS):\n" +
        $"W Warning (red) — {DefaultContextBudget.Warning}\n" +
        $"C Caution (amber) — {DefaultContextBudget.Caution}\n" +
        $"A Advisory (blue/quiet) — {DefaultContextBudget.Advisory}\n" +
        $"Habit — {DefaultContextBudget.Habit}";

    private static readonly ContinuityDto DefaultContinuity = new()
    {
        DefaultMove =
            "Before deep topic: memory_session_route_context and/or scratch checkpoint-*.handoff.md; jsonl only if readable handoff has a hole.",
        AgentIdeCanon =
            "knowledge/work/projects/door-to-singularity/cascade-ide/note-cdp-agent-ide-six-pillars-v0.md",
        AgentEnv =
            "Agent Env first (CDP packs + radius gate); CIDE projector later. Self-continuation policy=ask until host enqueue exists.",
        ContextBudget = DefaultContextBudget
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

        var includePack = false;
        if (callArgs.TryGetValue("include_pack", out var ip))
        {
            includePack = ip.ValueKind is JsonValueKind.True
                || (ip.ValueKind == JsonValueKind.String
                    && bool.TryParse(ip.GetString(), out var pb) && pb);
        }

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

        // A default: no pack dump. Opt-in include_pack=true is C/W (fat definitions).
        PackPlaneResult? packPlane = includePack
            ? await BuildPackPlaneAsync(byDomain, packId, processId, procedureId).ConfigureAwait(false)
            : PackPlaneResult.Omitted(packId, processId, procedureId);

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

}
