#nullable enable
using System.Text.Json;
using System.Text.Json.Nodes;
using Cdp.Core;
using Cdp.Evidence;
using Cdp.ScriptableIde;
using CdpMcp.Backends;
using CdpMcp.IntentWorkspace;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Tool = ModelContextProtocol.Protocol.Tool;

namespace CdpMcp;

internal static partial class MetaDispatch
{
    static async Task<string?> CoreAsync(
        MetaDispatchDeps d,
        string name,
        IReadOnlyDictionary<string, JsonElement> callArgs,
        CancellationToken cancellationToken,
        object? warm = null)
    {
        var session = d.Session;
        var docStore = d.DocStore;
        var byDomain = d.ByDomain;
        var modules = d.Modules;
        var allAffordances = d.AllAffordances;
        var settings = d.Settings;
        var mcpVersion = d.McpVersion;
        var SoftOrganMetaNames = d.SoftOrganMetaNames;
        var Pretty = d.Pretty;
        var shellHabitat = d.ShellHabitat;
        var mcpOutlet = d.McpOutlet;
        var internetBrowser = d.InternetBrowser;
        var ideSettings = d.IdeSettings;
        var workspaceStore = d.WorkspaceStore;
        var workspaceState = d.WorkspaceState;
        var workspaceDbPath = d.WorkspaceDbPath;
        var serverRef = d.ServerRef;
        var NotifyListChanged = d.NotifyListChanged;
        var EnsureOpenRecentWired = d.EnsureOpenRecentWired;
        var EnsureWorkspaceDb = d.EnsureWorkspaceDb;
        var BuildVisibleTools = d.BuildVisibleTools;
        var BuildMetaTools = d.BuildMetaTools;
        var DispatchAsync = d.DispatchToolAsync;
        var DispatchCdpWork = d.DispatchCdpWork;

        switch (name)
        {
    case "cdp_man":
        if (callArgs.TryGetValue("tool", out var t) && t.GetString() is { Length: > 0 } tool)
        {
            if (tool is "context_budget" or "budget" or "context")
                return SessionPlane.ContextBudgetManual;
            return $"Manual: {tool} — see tool description; domain ops via prefixed tools / sibling man.";
        }
        return "TOC: cdp_cockpit (hub where-am-I), cdp_session (A omnibus; include_pack=true for pack dogfood), cdp_health(explain_tool?), cdp_capabilities, " +
               "cdp_context(phase,object,intent?,language?), cdp_open(path), cdp_editor_scene|cdp_edit_sniper|cdp_edit_plan (map→aim→slices), " +
               "cdp_build|cdp_run|cdp_test|cdp_test_scene|cdp_test_plan (session IDE lifecycle), " +
               "cdp_analysis_scene (code analysis domain; feature=clones), " +
               "cdp_script_scene (script habitat put→diags→run), " +
               "cdp_ps1_scene (PS ISE put→check→run), " +
               "cdp_goto (Ctrl+T code + Ctrl+Q features → land/peek), " +
               "cdp_buffer(op=scene|open|read|edit|diagnostics|close) file buffer SSOT; edit returns diagnostics, " +
               "cdp_debug(op=scene|bp_add|bp_remove|bp_set|bp_list|bp_clear|launch|…) debug plane; session defaults, not breakpoints JSON, " +
               "cdp_pkg_find|list|add|remove|update|outdated, cdp_project_scene|create|list|close|add_to_sln, " +
               "cdp_sln_create|list|projects|add|remove, " +
               "cdp_work(op=intent|stage|scene), cdp_tools(... palette), " +
               "IDE: go_to_definition|find_usages|get_document_symbols|get_symbol_at_position|get_diagnostics|get_completions|get_signature_help|find|find_in_files|take|resolve_project_root|get_workspace_navigation_context, " +
               "cdp_csx_help / cdp_csx_check / cdp_csx_run / cdp_csx_run_plan / promote / discard. " +
               "cdp_shell_scene|run|history|rerun|last|which|kill|close (agent terminal; background long-run). " +
               "Pack: get_definition|list_pack|get_process|get_procedure|radius_gate_check. " +
               "Domain prefixes: memory_world_ memory_project_ memory_task_ memory_session_ memory_skill_ " +
               "memory_self_finding_ memory_self_failure_ debug_ build_ roslyn_ git_ codebase_index_ anui_. " +
               "Agent-IDE pillars: session plane, shared truth, affordance nav, continuity, evidence-first, self-ops. " +
               "Order: Agent Env first; CIDE projector later. " +
               "Context: man tool=context_budget (EICAS W/C/A).";
    case "cdp_health":
        return HealthJson(d, callArgs);
    case "cdp_capabilities":
        return JsonSerializer.Serialize(new
        {
            catalog = "f(phase,object[,language]); intent ranks",
            phases = Enum.GetNames<CdpPhase>().Select(x => x.ToLowerInvariant()),
            objects = Enum.GetNames<CdpObjectKind>().Select(x => x.ToLowerInvariant()),
            intents = Enum.GetNames<CdpIntent>().Select(x => x.ToLowerInvariant()),
            languages = settings.Languages.Ids,
            affordances = allAffordances.Length,
            domains = byDomain.Keys.OrderBy(x => x).ToArray(),
            list_tools_count = BuildVisibleTools().Count,
            meta_tool_names = BuildMetaTools()
                .Where(t => !SoftOrganMetaNames.Contains(t.Name))
                .Select(t => t.Name)
                .ToArray(),
            soft_organ_meta_hidden = SoftOrganMetaNames.OrderBy(x => x).ToArray(),
            buffer_tool = BuildMetaTools()
                .Where(t => t.Name == "cdp_buffer")
                .Select(t => new
                {
                    name = t.Name,
                    description = t.Description,
                    input_schema = t.InputSchema
                })
                .FirstOrDefault(),
            debug_tool = BuildMetaTools()
                .Where(t => t.Name == "cdp_debug")
                .Select(t => new
                {
                    name = t.Name,
                    description = t.Description,
                    input_schema = t.InputSchema
                })
                .FirstOrDefault(),
            layers = new
            {
                memory = new
                {
                    world = FacetCap(settings.Memory.World),
                    project = FacetCap(settings.Memory.Project),
                    task = ToggleCap(settings.Memory.Task),
                    session = ToggleCap(settings.Memory.Session),
                    skill = FacetCap(settings.Memory.Skill),
                    self = new
                    {
                        finding = ToggleCap(settings.Memory.Self.Finding),
                        failure = ToggleCap(settings.Memory.Self.Failure)
                    }
                },
                dev = new
                {
                    debug = ToggleCap(settings.Dev.Debug),
                    build = ToggleCap(settings.Dev.Build),
                    roslyn = ToggleCap(settings.Dev.Roslyn),
                    git = ToggleCap(settings.Dev.Git),
                    codebase_index = ToggleCap(settings.Dev.CodebaseIndex),
                    anui = ToggleCap(settings.Dev.Anui)
                }
            }
        }, Pretty);
    case "cdp_context":
        if (callArgs.TryGetValue("get", out var g) && g.ValueKind == JsonValueKind.True)
            return session.ToJson();
        var changed = false;
        string? layoutApplied = null;
        if (callArgs.TryGetValue("phase", out var ph) && CdpEnumParse.TryParsePhase(ph.GetString(), out var newPhase))
        {
            var oldPhaseWire = CdpEnumParse.ToWire(session.Phase);
            var phaseChanged = newPhase != session.Phase;
            session.Phase = newPhase;
            changed = true;
            if (phaseChanged)
            {
                EnsureWorkspaceDb();
                IdeStageCycle.TryPhaseTransition(oldPhaseWire, CdpEnumParse.ToWire(newPhase));
                layoutApplied = IdePhaseLayout.TryApplyForPhase(newPhase, callArgs);
            }
        }
        if (callArgs.TryGetValue("object", out var ob) && CdpEnumParse.TryParseObject(ob.GetString(), out var newObj))
        {
            session.Object = newObj;
            changed = true;
        }
        if (callArgs.TryGetValue("intent", out var it))
        {
            var s = it.GetString();
            if (string.IsNullOrWhiteSpace(s))
                session.Intent = null;
            else if (CdpEnumParse.TryParseIntent(s, out var newIntent))
                session.Intent = newIntent;
            changed = true;
        }
        if (callArgs.TryGetValue("language", out var langEl))
        {
            var ls = langEl.GetString();
            if (string.IsNullOrWhiteSpace(ls))
                session.Language = null;
            else if (settings.Languages.TryNormalize(ls, out var newLang))
                session.Language = CdpLanguages.IsAny(newLang) ? null : newLang;
            changed = true;
        }
        if (changed)
            NotifyListChanged();
        var ctxTail = changed ? "\n# list_changed: shortlist refreshed for new context" : "";
        if (layoutApplied is { Length: > 0 })
            ctxTail += $"\n# desk_layout: {layoutApplied} (phase SA; hold=layout_hold|desk.layout.hold)";
        else if (changed
                 && callArgs.ContainsKey("phase")
                 && IdePhaseLayout.IsHold(callArgs))
            ctxTail += "\n# desk_layout: held";
        return session.ToJson() + ctxTail;
    case "cdp_open":
    {
        EnsureOpenRecentWired();
        string? openPath = null;
        if (callArgs.TryGetValue("path", out var openPathEl) && openPathEl.GetString() is { Length: > 0 } op)
            openPath = op;
        else if (callArgs.TryGetValue("recent_index", out var riEl) && riEl.TryGetInt32(out var ri))
        {
            var hit = OpenRecentStore.TryGet(ri)
                ?? throw new ArgumentException($"No Open Recent entry at index {ri}.");
            openPath = hit.Path;
        }
        else
        {
            var hit = OpenRecentStore.TryGet(0)
                ?? throw new ArgumentException(
                    "path is required for cdp_open (or pass recent_index / open something first so Recent is non-empty).");
            openPath = hit.Path;
        }

        var open = settings.Languages.Detect(openPath);
        var park = docStore.ParkOutsideProject(open.Root);
        var payload = IdeLanguageTools.ApplyOpen(session, open, park);
        shellHabitat.SyncSessionCwd(session.ProjectRoot);
        DeskBookmark.Save(session, docStore);
        NotifyListChanged();
        // HCI-like: warm MSBuild workspace once for csharp session (background).
        if (string.Equals(session.Language, "csharp", StringComparison.OrdinalIgnoreCase)
            && session.SolutionOrProjectPath is { Length: > 0 } warmPath)
        {
            var pathCopy = warmPath;
            _ = Task.Run(async () =>
            {
                try
                {
                    await RoslynMcp.ServiceLayer.MsBuildWorkspaceHost.WarmAsync(pathCopy).ConfigureAwait(false);
                }
                catch
                {
                    // Warm is best-effort; tools still open on demand.
                }
            });
        }

        return payload + "\n# list_changed: shortlist refreshed after cdp_open";
    }
    case "cdp_restore":
    {
        var restoreOp = "restore";
        if (callArgs.TryGetValue("op", out var ropEl) && ropEl.GetString() is { Length: > 0 } rop)
            restoreOp = rop.Trim();
        if (string.Equals(restoreOp, "peek", StringComparison.OrdinalIgnoreCase)
            || string.Equals(restoreOp, "status", StringComparison.OrdinalIgnoreCase))
            return DeskBookmark.PeekJson();

        return DeskBookmark.Restore(
            session,
            docStore,
            detectOpen: p => settings.Languages.Detect(p),
            syncShellCwd: () => shellHabitat.SyncSessionCwd(session.ProjectRoot),
            notifyListChanged: NotifyListChanged) + "\n# list_changed: shortlist refreshed after cdp_restore";
    }
    case "cdp_deploy":
        return IdeDeploy.Run(session, callArgs);
    case "cdp_elicit":
        return await IdeElicit.RunAsync(serverRef, callArgs, cancellationToken).ConfigureAwait(false);
    case "cdp_land":
    {
        return await NavigationLand.RunAsync(
                callArgs,
                session,
                docStore,
                detectOpen: p => settings.Languages.Detect(p),
                syncShellCwd: () => shellHabitat.SyncSessionCwd(session.ProjectRoot),
                notifyListChanged: NotifyListChanged,
                dispatchTool: DispatchAsync,
                cancellationToken)
            .ConfigureAwait(false);
    }
    case "cdp_cide_presentation":
        return IdeCidePresentationChannel.HandleJson(callArgs);
    case "cdp_intercom":
        return IdeCideIntercomChannel.HandleJson(callArgs);
    case "cdp_citizen":
        return IdeCitizenChannel.HandleJson(callArgs);
    case "cdp_mcp":
        return await mcpOutlet.DispatchAsync(callArgs, cancellationToken).ConfigureAwait(false);
    case "cdp_browser":
        return internetBrowser.Dispatch(callArgs);
    case "cdp_settings":
        return ideSettings.Dispatch(callArgs);
    case "cdp_search":
        return IdeFindChannel.HandleJson(docStore, session, callArgs);
        default:
            return null;

        }
    }
}
