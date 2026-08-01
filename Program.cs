using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cdp.Core;
using Cdp.Evidence;
using Cdp.ScriptableIde;
using CdpMcp;
using CdpMcp.Backends;
using CdpMcp.IntentWorkspace;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OutWit.Database.EntityFramework.Extensions;
using Tool = ModelContextProtocol.Protocol.Tool;

var configPath = args.SkipWhile(a => a != "--config").Skip(1).FirstOrDefault()
    ?? Environment.GetEnvironmentVariable("CDP_MCP_CONFIG")
    ?? Path.Combine(AppContext.BaseDirectory, "config", "cdp-mcp.toml");
var settings = CdpSettings.Load(configPath);
IdeLanguageTools.Configure(settings.Languages, settings.LspPresets);
IdeCockpitHostChannel.Configure(settings.CockpitHost);
VendorCatalog.Configure(settings.Vendor);
IdeIgniteArmHost.EnsureStarted();

var workspaceDbPathOverride = settings.IntentWorkspace.DatabasePath;
var workspaceDbPath = workspaceDbPathOverride
    ?? Path.Combine(CdpProfile.StateRoot, "intent-workspace.witdb");
IntentWorkspaceStore? workspaceStore = null;
var workspaceState = new IntentWorkspaceState { DatabasePath = workspaceDbPath };
string? openedWorkspaceDbPath = null;

void InvalidateWorkspaceScope()
{
    IdeSettingsStore.Invalidate();
    workspaceStore = null;
    openedWorkspaceDbPath = null;
}

CdpProfile.OnStateRootChanged(InvalidateWorkspaceScope);

var session = new SessionContext();
IdeCockpitHostChannel.ProjectRootResolver = () => session.ProjectRoot;

void EnsureWorkspaceDb()
{
    CdpClientWorkspace.EnsureSessionFallback(session);
    var path = workspaceDbPathOverride
        ?? Path.Combine(CdpProfile.StateRoot, "intent-workspace.witdb");
    if (workspaceStore is not null &&
        string.Equals(openedWorkspaceDbPath, path, StringComparison.OrdinalIgnoreCase))
        return;

    workspaceDbPath = path;
    workspaceState.DatabasePath = path;
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    var wsOptions = new DbContextOptionsBuilder<IntentWorkspaceDbContext>()
        .UseWitDb($"Data Source={path}")
        .Options;
    using (var bootGate = IntentWorkspaceStore.EnterFileGate(path))
    using (var boot = new IntentWorkspaceDbContext(wsOptions))
        boot.Database.EnsureCreated();
    workspaceStore = new IntentWorkspaceStore(wsOptions, path);
    workspaceStore.EnsureOpenRecentTable();
    workspaceStore.MigrateLegacyOpenRecentJsonIfPresent();
    workspaceStore.EnsureDeskSeatsTable();
    workspaceStore.MigrateLegacyDeskSeatsJsonIfPresent();
    workspaceStore.EnsureStagePhaseAffinityColumn();
    workspaceStore.EnsureStageClockColumns();
    workspaceStore.EnsureStageProductColumn();
    workspaceStore.EnsureStageEventsTable();
    workspaceStore.EnsureStageCriteriaTable();
    workspaceStore.EnsureWorkFocusTable();
    workspaceStore.WorkFocusHydrate(workspaceState);
    workspaceStore.EnsureScriptLastRunTable();
    IdeDeskSeats.Bind(workspaceStore);
    ScriptScene.Bind(workspaceStore);
    IdeStageCycle.Bind(workspaceStore, () => workspaceState, () => CdpEnumParse.ToWire(session.Phase));
    OpenRecentStore.Configure(new WitDbOpenRecentBackend(workspaceStore, path));
    openedWorkspaceDbPath = path;
}

IdeIgniteArmHost.BindFlightProbe(() =>
{
    if (workspaceState.ActiveStageId is null)
        return ContinuityFlight.NoActiveTask;
    try
    {
        EnsureWorkspaceDb();
        return IdeTaskManager.ProbeContinuityFlight(workspaceStore, workspaceState);
    }
    catch
    {
        return ContinuityFlight.Fly;
    }
});

/// <summary>Open Recent lives in WitDB — ensure store before push/list (cdp_open / CSX Open.*).</summary>
void EnsureOpenRecentWired()
{
    EnsureWorkspaceDb();
}

IntentWorkspaceStore RequireWorkspace()
{
    EnsureWorkspaceDb();
    return workspaceStore!;
}

var modules = new List<ICdpBackendModule>();
var notesRuntime = SharedNotesRuntime.TryCreate(settings);
if (notesRuntime is not null)
{
    if (settings.Memory.World.Enabled) modules.Add(new MemoryWorldBackend(notesRuntime, settings));
    if (settings.Memory.Project.Enabled) modules.Add(new MemoryProjectBackend(notesRuntime, settings));
    if (settings.Memory.Skill.Enabled) modules.Add(new MemorySkillBackend(notesRuntime, settings));
    if (settings.Memory.Session.Enabled) modules.Add(new MemorySessionBackend(notesRuntime, settings));
}
if (settings.Memory.Task.Enabled) modules.Add(new TaskKnowledgeBackend(settings));
if (settings.Memory.Self.Finding.Enabled) modules.Add(new FindingsBackend(settings));
if (settings.Memory.Self.Failure.Enabled) modules.Add(new FailuresBackend(settings));
if (settings.Dev.Debug.Enabled) modules.Add(new DebugBackend(settings));
if (settings.Dev.Build.Enabled) modules.Add(new BuildTestBackend(settings));
if (settings.Dev.Roslyn.Enabled) modules.Add(new RoslynBackend(settings));
if (settings.Dev.Git.Enabled) modules.Add(new GitBackend(settings));
if (settings.Dev.CodebaseIndex.Enabled) modules.Add(new CodebaseIndexBackend(settings));
if (settings.Dev.Anui.Enabled) modules.Add(new AnuiBackend(settings));

var byDomain = modules.Where(m => m.IsEnabled).ToDictionary(m => m.Domain, StringComparer.Ordinal);
IdeReportJobRunner? jobRunner = null;
IdeReportJobRunner RequireJobRunner()
{
    var store = RequireWorkspace();
    return jobRunner ??= new IdeReportJobRunner(store, byDomain);
}

var allAffordances = modules.Where(m => m.IsEnabled).SelectMany(m => m.Affordances).ToArray();
var anTools = ToolCatalog.Build().ToDictionary(t => t.Name, StringComparer.Ordinal);
var tkTools = AgentTaskKnowledgeMcp.ToolCatalog.Build().ToDictionary(t => t.Name, StringComparer.Ordinal);
var findTools = AgentFindingsMcp.ToolCatalog.Build().ToDictionary(t => t.Name, StringComparer.Ordinal);
var failTools = AgentFailuresMcp.ToolCatalog.Build().ToDictionary(t => t.Name, StringComparer.Ordinal);
var dbgTools = DotnetDebugMcp.ToolCatalog.Build().ToDictionary(t => t.Name, StringComparer.Ordinal);
var btTools = DotnetBuildTestMcp.ToolCatalog.Build().ToDictionary(t => t.Name, StringComparer.Ordinal);
var roslynTools = RoslynMcp.ToolCatalog.Build().ToDictionary(t => t.Name, StringComparer.Ordinal);
var gitTools = GitMcp.ToolCatalog.Build().ToDictionary(t => t.Name, StringComparer.Ordinal);
var hciTools = HybridCodebaseIndex.Mcp.ToolCatalog.Build().ToDictionary(t => t.Name, StringComparer.Ordinal);
var anuiTools = Anui.Agent.Mcp.ToolCatalog.Build().ToDictionary(t => t.Name, StringComparer.Ordinal);

var docStore = new DocumentBufferStore();
using var diskSyncWatch = DocumentDiskSyncWatcher.Start(docStore);
using var intercomCannon = IntercomVoiceCannonWatcher.Start();
IdeIgniteArmHost.StartHildWatch();
IdeIgniteArmHost.StartOomWatch();
IdeLanguageTools.BindDocumentStore(docStore);
var shellHabitat = new TerminalMcp.Core.ShellHabitat();
shellHabitat.Finished += info =>
{
    IdeIgniteArmHost.Notify(
        "shell_finished",
        ok: info.ExitCode == 0,
        pulse: info.Tab,
        detail: info.Command.Length > 120 ? info.Command[..120] : info.Command);
    if (info.ExitCode != 0)
        IdeStageCycle.TryAppend("shell.fail", "shell", info.Command, info.Tab);
};
var mcpOutlet = new McpOutletHabitat();
var internetBrowser = new InternetBrowserHabitat();
var ideSettings = new IdeSettingsHabitat(
    configPath,
    settings,
    session,
    shellHabitat,
    () => ShellDefaults(session));
IdeToolchainChannel.Configure(shellHabitat, () => ShellDefaults(session));
if (notesRuntime is not null && settings.Memory.Project.Enabled)
{
    var projectScope = new MemoryScopeGateway(
        CdpDomains.MemoryProject,
        settings.Memory.Project.Roots);
    var handlers = notesRuntime.Handlers;
    IdeLearnChannel.Configure((filePath, content) =>
    {
        var args = projectScope.Apply(
            "write_knowledge_file",
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["file_path"] = JsonSerializer.SerializeToElement(filePath),
                ["content"] = JsonSerializer.SerializeToElement(content),
                ["allow_shrink"] = JsonSerializer.SerializeToElement(true)
            });
        return handlers.Handle("write_knowledge_file", args);
    });
}
IdeFlightDataRecorder.BindContext(() => new IdeFlightDataRecorder.FdrContextSnap(
    Phase: CdpEnumParse.ToWire(session.Phase),
    Object: CdpEnumParse.ToWire(session.Object),
    Language: session.Language,
    ProjectLeaf: string.IsNullOrWhiteSpace(session.ProjectRoot)
        ? null
        : Path.GetFileName(session.ProjectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))));
if (CdpEnumParse.TryParsePhase(settings.DefaultPhase, out var dp)) session.Phase = dp;
if (CdpEnumParse.TryParseObject(settings.DefaultObject, out var dobj)) session.Object = dobj;
// User prefs can override cold phase/object after process defaults.
if (IdeSettingsStore.TryGet("session.default_phase", out var up)
    && CdpEnumParse.TryParsePhase(up, out var udp))
    session.Phase = udp;
if (IdeSettingsStore.TryGet("session.default_object", out var uo)
    && CdpEnumParse.TryParseObject(uo, out var udo))
    session.Object = udo;

var mcpVersion = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.4.0";
var Pretty = new JsonSerializerOptions { WriteIndented = true };
McpServer? serverRef = null;

/// Soft organs with go= aliases — schemas stay for CallTool, but omit from ListTools thrash.
/// Keep cdp_ignite + cdp_pressure visible (autonomy axes).
var SoftOrganMetaNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "cdp_search",
    "cdp_sa",
    "cdp_refactor",
    "cdp_debug_sa",
    "cdp_test_sa",
    "cdp_build_sa",
    "cdp_crm",
    "cdp_arch",
    "cdp_onboard",
    "cdp_toolchain",
    "cdp_files",
    "cdp_md_author",
    "cdp_learn",
    "cdp_domain",
    "cdp_fdr",
    "cdp_teeth",
    "cdp_postmortem",
    "cdp_scope",
    "cdp_webcam",
    "cdp_ps1_scene"
};

List<Tool> BuildVisibleTools()
{
    // Soft-organ Metas stay CallTool-routable but off always-ListTools (go= / cdp_cockpit).
    var meta = BuildMetaTools()
        .Where(t => !SoftOrganMetaNames.Contains(t.Name))
        .ToList();
    var ide = IdeLanguageTools.BuildBareVerbTools().ToList();
    var hits = PhaseObjectCatalog.Query(
        allAffordances, session.Phase, session.Object, session.Intent,
        limit: PhaseObjectCatalog.DefaultListToolsLimit, language: session.Language);
    var domainTools = new List<Tool>();
    foreach (var hit in hits)
    {
        var a = hit.Affordance;
        var schemaTool = ResolveSchema(a.Domain, a.UnderlyingName);
        if (schemaTool is null) continue;
        var schema = a.Domain == CdpDomains.Git
            ? GitSessionDefaults.OptionalWorkspaceSchema(schemaTool.InputSchema)
            : a.Domain == CdpDomains.CodebaseIndex
            ? CodebaseIndexSessionDefaults.OptionalSessionSchema(schemaTool.InputSchema)
            : a.Domain == CdpDomains.Build
            ? BuildSessionDefaults.OptionalSessionSchema(schemaTool.InputSchema)
            : MemorySessionDefaults.IsMemoryDomain(a.Domain)
            ? MemorySessionDefaults.OptionalWorkspaceSchema(schemaTool.InputSchema)
            : schemaTool.InputSchema;
        domainTools.Add(new Tool
        {
            Name = a.PrefixedName,
            Description = $"[{a.Domain}] {schemaTool.Description}",
            InputSchema = schema
        });
    }
    return meta.Concat(ide).Concat(domainTools).ToList();
}

Tool? ResolveSchema(string domain, string underlying) => domain switch
{
    CdpDomains.MemoryWorld or CdpDomains.MemoryProject or CdpDomains.MemorySkill or CdpDomains.MemorySession
        => anTools.GetValueOrDefault(underlying),
    CdpDomains.MemoryTask => tkTools.GetValueOrDefault(underlying),
    CdpDomains.MemorySelfFinding => findTools.GetValueOrDefault(underlying),
    CdpDomains.MemorySelfFailure => failTools.GetValueOrDefault(underlying),
    CdpDomains.Debug => dbgTools.GetValueOrDefault(underlying),
    CdpDomains.Build => btTools.GetValueOrDefault(underlying),
    CdpDomains.Roslyn => roslynTools.GetValueOrDefault(underlying),
    CdpDomains.Git => gitTools.GetValueOrDefault(underlying),
    CdpDomains.CodebaseIndex => hciTools.GetValueOrDefault(underlying),
    CdpDomains.Anui => anuiTools.GetValueOrDefault(underlying),
    _ => null
};

List<Tool> BuildMetaTools() => MetaToolCatalog.Build();


const string DomainPrefixHint =
    "memory_world_|memory_project_|memory_task_|memory_session_|memory_skill_|" +
    "memory_self_finding_|memory_self_failure_|debug_|build_|roslyn_|git_|codebase_index_|anui_";

var options = new McpServerOptions
{
    ServerInfo = new Implementation { Name = "CdpMcp", Version = mcpVersion },
    ProtocolVersion = "2024-11-05",
    ServerInstructions =
        "Cognitive Dev Platform = agent-IDE substrate (not pixel IDE). " +
        "catalog=f(phase,object[,language]); intent ranks. " +
        "Lifecycle: recall → explore → clarify → plan → act → verify → handoff. " +
        "Cold ListTools = recall+kb (known memory pull; not browse). " +
        "After MCP restart: call cdp_session or cdp_context first so ListTools refreshes (pack tools). " +
        "Pack dogfood: memory_world_get_definition|get_process|get_procedure|list_pack|radius_gate_check (epistemic-scene). " +
        "Always: cdp_cockpit (desk seats P|F|M + cmd= REPL: next[]+go=) / cdp_session (omnibus) / cdp_context / cdp_open / cdp_restore (Restore Previous desk) / cdp_deploy (dual-instance publish; go=deploy) / cdp_land (Family:navigation Anchor land) / cdp_mcp (MCP outlet scene/mount/call) / cdp_browser (internet lynx: scene_internet_browser) / cdp_settings (Tools→Options: go=options) / cdp_editor_scene|cdp_edit_plan / cdp_buffer(op) / cdp_debug(op) / cdp_recent / cdp_build|cdp_run|cdp_test / cdp_pkg_* / cdp_work (intent scenes) / cdp_tools (palette) / cdp_health (explain_tool?). " +
        "Mutate SSOT: cdp_buffer (open|create|edit); Instant Save flush=true on edit/close (flush=false batches; close discard=true to drop). Relative path= → ProjectRoot after cdp_open. Prefer edit_op=anchor [F:;M:;K:] for csharp. Cursor host Write bypasses PathMutateGate. " +
        "Buffer plane: cdp_buffer op=open|edit|… — edit returns diagnostics in-result (almost-online while you keep the turn). " +
        "Debug plane: cdp_debug op=bp_add|launch|stop_context|… — session defaults after cdp_open; .csproj is BP key, launch resolves dll under bin/; JSON file is storage only. " +
        "IDE verbs (harness routes LSP): go_to_definition, find_usages, get_document_symbols, get_symbol_at_position, get_diagnostics, resolve_project_root, get_workspace_navigation_context. " +
        "Prefer cdp_build/cdp_run/cdp_test/cdp_pkg_*/cdp_project_*/cdp_sln_* over shell for session project. " +
        "Agent shell habitat: cdp_shell_* = primary IDE terminal; sibling terminal-mcp (terminal_*) = escape only. " +
        "CSX: cdp_script_scene (put→diags→check→run) | cdp_csx_help | cdp_csx_check | cdp_csx_run | cdp_csx_run_plan | promote | discard | cdp_evidence. " +
        "PS1: cdp_ps1_scene (ISE put→AST check→pwsh -File→last). " +
        "Domain tools prefixed " + DomainPrefixHint + " (roslyn_* = legacy aliases; prefer bare IDE verbs). " +
        "ListTools = core meta + bare IDE verbs + ≤10 domain shortlist (soft-organ Metas via go=/CallTool; not always-ListTools). " +
        "Too many tools = agent thrash — use cdp_context to retarget, cdp_tools to preview, cdp_session (A; include_pack=true only when needed). " +
        "Continuity: route/handoff before deep topic; evidence-first (stop_context), PNG last.",
    Capabilities = new ServerCapabilities
    {
        Tools = new ToolsCapability { ListChanged = true }
    },
    Handlers = new McpServerHandlers
    {
        ListToolsHandler = (_, _) => ValueTask.FromResult(new ListToolsResult { Tools = BuildVisibleTools() }),
        CallToolHandler = async (request, cancellationToken) =>
        {
            var name = request.Params?.Name ?? "";
            var callArgs = request.Params?.Arguments is IReadOnlyDictionary<string, JsonElement> d
                ? d
                : FrozenDictionary<string, JsonElement>.Empty;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Keep isolation key fresh (client roots when advertised).
                if (serverRef is not null)
                    await CdpClientWorkspace.RefreshAsync(serverRef, cancellationToken).ConfigureAwait(false);
                CdpClientWorkspace.EnsureSessionFallback(session);
                var text = await IdeToolCallWatch.RunAsync(
                        name,
                        callArgs,
                        ct => IdeCommandModule.ExecuteAsync(name, callArgs, ct),
                        cancellationToken)
                    .ConfigureAwait(false);
                return new CallToolResult
                {
                    Content = ToolMediaOutbox.BuildContent(text)
                };
            }
            catch (OperationCanceledException)
            {
                ToolMediaOutbox.Clear();
                return new CallToolResult
                {
                    Content = [new TextContentBlock { Text = $"# Aborted: {(string.IsNullOrEmpty(name) ? "(unknown)" : name)}" }]
                };
            }
            catch (Exception ex)
            {
                ToolMediaOutbox.Clear();
                return new CallToolResult
                {
                    Content = [new TextContentBlock { Text = $"Error: {ex.Message}" }],
                    IsError = true
                };
            }
        }
    }
};

async Task<string> DispatchAsync(
    string name,
    IReadOnlyDictionary<string, JsonElement> callArgs,
    CancellationToken cancellationToken)
{
    // Sticky desk: cold tools hydrate bookmark under the hood (once/process).
    var warm = DeskWarm.TryWarm(
        name,
        session,
        docStore,
        detectOpen: p => settings.Languages.Detect(p),
        syncShellCwd: () => shellHabitat.SyncSessionCwd(session.ProjectRoot),
        notifyListChanged: NotifyListChanged,
        callArgs);

    if (DocumentEditPlane.IsDocTool(name))
        return await DocumentEditPlane.DispatchAsync(name, docStore, session, byDomain, callArgs, cancellationToken)
            .ConfigureAwait(false);

    if (EditorPlane.IsEditorTool(name))
        return await EditorPlane.DispatchAsync(name, docStore, session, byDomain, callArgs, cancellationToken)
            .ConfigureAwait(false);

    if (AnalysisScene.IsAnalysisTool(name))
        return await AnalysisScene.DispatchAsync(docStore, session, byDomain, callArgs, cancellationToken)
            .ConfigureAwait(false);

    if (ScriptScene.IsScriptTool(name))
        return await ScriptScene.DispatchAsync(
                docStore, session, byDomain, callArgs,
                (n, a, ct) => DispatchMetaAsync(n, a, ct),
                cancellationToken)
            .ConfigureAwait(false);

    if (Ps1Scene.IsPs1Tool(name))
        return await Ps1Scene.DispatchAsync(docStore, session, byDomain, callArgs, cancellationToken)
            .ConfigureAwait(false);

    if (GoToAll.IsGoToTool(name))
        return GoToAll.Dispatch(docStore, session, callArgs);

    if (DebugPlane.IsDebugPlaneTool(name))
        return await DebugPlane.DispatchAsync(session, byDomain, callArgs, cancellationToken)
            .ConfigureAwait(false);

    if (name.StartsWith("cdp_", StringComparison.Ordinal))
        return await DispatchMetaAsync(name, callArgs, cancellationToken, warm).ConfigureAwait(false);

    if (IdeLanguageTools.IsBareVerb(name))
        return await IdeLanguageTools.DispatchBareAsync(name, session, byDomain, callArgs, cancellationToken)
            .ConfigureAwait(false);

    if (!CdpDomains.TrySplit(name, out var domain, out var underlying))
        throw new ArgumentException($"Unknown tool: {name}");
    if (!byDomain.TryGetValue(domain, out var mod))
        throw new ArgumentException($"Backend '{domain}' not mounted.");
    if (domain == CdpDomains.Git)
        callArgs = GitSessionDefaults.WithWorkspace(callArgs, session);
    else if (domain == CdpDomains.CodebaseIndex)
        callArgs = CodebaseIndexSessionDefaults.WithSession(callArgs, session);
    else if (domain == CdpDomains.Build)
        callArgs = BuildSessionDefaults.WithSession(callArgs, session);
    else if (MemorySessionDefaults.IsMemoryDomain(domain))
        callArgs = MemorySessionDefaults.WithWorkspace(callArgs, session);
    underlying = CdpDomains.ExpandUnderlying(domain, underlying);
    return await mod.CallAsync(underlying, callArgs).ConfigureAwait(false);
}

async Task<string> DispatchMetaAsync(
    string name,
    IReadOnlyDictionary<string, JsonElement> callArgs,
    CancellationToken cancellationToken,
    object? warm = null)
{
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
        {
            object? explain = null;
            if (callArgs.TryGetValue("explain_tool", out var eht) && eht.GetString() is { Length: > 0 } en)
                explain = SessionPlane.ExplainTool(en, session, byDomain, allAffordances);

            var asm = typeof(Program).Assembly;
            var exePath = Environment.ProcessPath ?? asm.Location;
            DateTimeOffset? buildUtc = null;
            try
            {
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                    buildUtc = File.GetLastWriteTimeUtc(exePath);
            }
            catch { /* ignore */ }

            object? pendingUpdate = null;
            try
            {
                var dir = Path.GetDirectoryName(exePath);
                if (dir is { Length: > 0 })
                {
                    var pendingPath = Path.Combine(dir, "cdp-pending-update.json");
                    if (File.Exists(pendingPath))
                        pendingUpdate = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(pendingPath));
                }
            }
            catch
            {
                pendingUpdate = new { ok = false, error = "pending_update_unreadable" };
            }

            return JsonSerializer.Serialize(new
            {
                ok = true,
                runtime = new
                {
                    version = mcpVersion,
                    version_full = asm.GetName().Version?.ToString(),
                    exe_path = exePath,
                    build_utc = buildUtc?.ToString("o"),
                    pending_update = pendingUpdate
                },
                continuity = IdeIgniteArmHost.ContinuitySlice(),
                continuity_pulse = IdeIgniteArmHost.ContinuityPulseLine(),
                isolation = CdpClientWorkspace.StatusCard(),
                ops = IdeOpsPulse.Snap(),
                ops_pulse = IdeOpsPulse.Line(),
                teeth_pulse = IdeTeethChannel.PulseLine(),
                backends = modules.Select(m => new { domain = m.Domain, enabled = m.IsEnabled, health = m.HealthSummary }),
                typescript_worker = IdeLanguageTools.TsHealth(),
                lsp = IdeLanguageTools.LspHealth(),
                project = new
                {
                    root = session.ProjectRoot,
                    kind = session.ProjectKind,
                    language = session.Language,
                    solution_or_project_path = session.SolutionOrProjectPath,
                    tsconfig_path = session.TsConfigPath
                },
                explain_tool = explain,
                recovery_note =
                    "Prefer go=deploy / cdp_deploy from the survivor seat (sibling Target). " +
                    "Hard KillRunning + CDP_RELOAD_NUDGE (kj-1349) unless -NoNudgeMcp. " +
                    "Fallback: human Reload. Soft stages <target>.next + cdp-pending-update.json. " +
                    "Cold tools auto-warm desk bookmark once/process. Prefer cdp_health + explain_tool before guessing."
            }, Pretty);
        }
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
        case "cdp_sa":
            return IdeSaChannel.HandleJson(docStore, session, callArgs);
        case "cdp_refactor":
            return IdeRefactorPlanChannel.HandleJson(docStore, session, callArgs);
        case "cdp_debug_sa":
            return IdeDebugSaChannel.HandleJson(session, callArgs);
        case "cdp_test_sa":
            return IdeTestSaChannel.HandleJson(session, callArgs);
        case "cdp_build_sa":
            return IdeBuildSaChannel.HandleJson(session, callArgs);
        case "cdp_crm":
            return IdeCrmChannel.HandleJson(session, workspaceStore, workspaceState, callArgs);
        case "cdp_arch":
            return IdeArchBoardChannel.HandleJson(session, callArgs);
        case "cdp_onboard":
            return IdeOnboardChannel.HandleJson(session, callArgs);
        case "cdp_toolchain":
            return IdeToolchainChannel.HandleJson(session, callArgs);
        case "cdp_md_author":
            return IdeMdAuthorChannel.HandleJson(session, callArgs);
        case "cdp_fdr":
            return IdeFdrChannel.HandleJson(session, callArgs);
        case "cdp_teeth":
            return IdeTeethChannel.HandleJson(session, callArgs);
        case "cdp_postmortem":
            return IdePostmortemChannel.HandleJson(session, callArgs);
        case "cdp_learn":
            return IdeLearnChannel.HandleJson(session, callArgs);
        case "cdp_scope":
            return IdeScopeChannel.HandleJson(session, callArgs);
        case "cdp_files":
            return IdeFilesChannel.HandleJson(docStore, session, callArgs);
        case "cdp_ignite":
            return await IdeIgniteChannel.HandleJsonAsync(callArgs, cancellationToken);
        case "cdp_webcam":
            return IdeWebcamChannel.HandleJson(session, callArgs);
        case "cdp_pressure":
            return IdePressureChannel.HandleJson(session, callArgs);
        case "cdp_domain":
            return IdeDomainChannel.HandleJson(session, callArgs);
        case "cdp_icm":
            return await IdeIcmChannel.HandleJsonAsync(callArgs, cancellationToken);
        case "cdp_cockpit_host":
            return IdeCockpitHostChannel.HandleJson(callArgs);
        case "cdp_recent":
        {
            EnsureOpenRecentWired();
            var take = 12;
            if (callArgs.TryGetValue("take", out var takeEl) && takeEl.TryGetInt32(out var ti) && ti > 0)
                take = ti;
            var items = OpenRecentStore.List(take);
            return JsonSerializer.Serialize(new
            {
                count = items.Count,
                store = OpenRecentStore.Location,
                store_kind = "witdb",
                items = items.Select((e, i) => new
                {
                    index = i,
                    path = e.Path,
                    root = e.Root,
                    kind = e.Kind,
                    language = e.Language,
                    opened_utc = e.OpenedUtc
                })
            }, Pretty);
        }
        case "cdp_build":
            return await IdeSessionLifecycle.BuildAsync(
                session, callArgs, byDomain.GetValueOrDefault("build"), cancellationToken).ConfigureAwait(false);
        case "cdp_test":
            return await IdeSessionLifecycle.TestAsync(
                session, callArgs, byDomain.GetValueOrDefault("build"), cancellationToken).ConfigureAwait(false);
        case "cdp_test_scene":
            return await IdeSessionLifecycle.TestSceneAsync(
                session, callArgs, byDomain.GetValueOrDefault("build"), cancellationToken).ConfigureAwait(false);
        case "cdp_test_plan":
            return await IdeSessionLifecycle.TestPlanAsync(
                session, callArgs, byDomain.GetValueOrDefault("build"), cancellationToken).ConfigureAwait(false);
        case "cdp_run":
            return await IdeSessionLifecycle.RunAsync(session, callArgs, cancellationToken).ConfigureAwait(false);
        case "cdp_pkg_find":
        {
            var q = callArgs.TryGetValue("query", out var qEl) ? qEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(q))
                throw new ArgumentException("query is required.");
            var take = 5;
            if (callArgs.TryGetValue("take", out var tEl) && tEl.TryGetInt32(out var ti))
                take = ti;
            var (bus, plan) = PackageSession(session, callArgs);
            return (await PackageOps.FindAsync(bus, plan, q!, take, cancellationToken).ConfigureAwait(false)).ToJson();
        }
        case "cdp_pkg_list":
        {
            var (bus, plan) = PackageSession(session, callArgs);
            var path = OptionalPath(callArgs);
            return (await PackageOps.ListAsync(bus, plan, path, cancellationToken).ConfigureAwait(false)).ToJson();
        }
        case "cdp_pkg_add":
        {
            var id = callArgs.TryGetValue("id", out var idEl) ? idEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("id is required.");
            var ver = callArgs.TryGetValue("version", out var vEl) ? vEl.GetString() : null;
            var (bus, plan) = PackageSession(session, callArgs);
            return (await PackageOps.AddAsync(bus, plan, id!, ver, OptionalPath(callArgs), cancellationToken).ConfigureAwait(false)).ToJson();
        }
        case "cdp_pkg_remove":
        {
            var id = callArgs.TryGetValue("id", out var idEl) ? idEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("id is required.");
            var (bus, plan) = PackageSession(session, callArgs);
            return (await PackageOps.RemoveAsync(bus, plan, id!, OptionalPath(callArgs), cancellationToken).ConfigureAwait(false)).ToJson();
        }
        case "cdp_pkg_update":
        {
            var id = callArgs.TryGetValue("id", out var idEl) ? idEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("id is required.");
            var ver = callArgs.TryGetValue("version", out var vEl) ? vEl.GetString() : null;
            var (bus, plan) = PackageSession(session, callArgs);
            return (await PackageOps.UpdateAsync(bus, plan, id!, ver, OptionalPath(callArgs), cancellationToken).ConfigureAwait(false)).ToJson();
        }
        case "cdp_pkg_outdated":
        {
            var (bus, plan) = PackageSession(session, callArgs);
            return (await PackageOps.OutdatedAsync(bus, plan, OptionalPath(callArgs), cancellationToken).ConfigureAwait(false)).ToJson();
        }
        case "cdp_project_scene":
        {
            var (bus, plan) = PackageSession(session, callArgs);
            var root = callArgs.TryGetValue("root", out var rEl) ? rEl.GetString() : null;
            var includeInstalled = callArgs.TryGetValue("include_installed", out var ii)
                && ii.ValueKind == JsonValueKind.True;
            var maxExisting = callArgs.TryGetValue("max_existing", out var me) && me.TryGetInt32(out var mei)
                ? mei : ProjectScene.MaxExistingDefault;
            var maxInstalled = callArgs.TryGetValue("max_installed", out var mi) && mi.TryGetInt32(out var mii)
                ? mii : ProjectScene.MaxInstalledDefault;
            return (await ProjectOps.SceneAsync(bus, plan, root, includeInstalled, maxExisting, maxInstalled, cancellationToken)
                .ConfigureAwait(false)).ToJson();
        }
        case "cdp_project_create":
        {
            if (!callArgs.TryGetValue("output_dir", out var odEl) || odEl.GetString() is not { Length: > 0 } outputDir)
                throw new ArgumentException("output_dir is required.");
            var projName = callArgs.TryGetValue("name", out var nEl) ? nEl.GetString() : null;
            var template = callArgs.TryGetValue("template", out var tEl) && tEl.GetString() is { Length: > 0 } tmpl
                ? tmpl
                : "console";
            var policyRaw = callArgs.TryGetValue("tfm_policy", out var pEl) ? pEl.GetString() : null;
            var policy = TfmResolver.ParsePolicy(policyRaw);
            var tfm = callArgs.TryGetValue("tfm", out var fEl) ? fEl.GetString() : null;
            var engPolRaw = callArgs.TryGetValue("engine_policy", out var epEl) ? epEl.GetString() : null;
            var engPolicy = EngineResolver.ParsePolicy(engPolRaw);
            var engines = callArgs.TryGetValue("engines", out var eEl) ? eEl.GetString() : null;
            var force = callArgs.TryGetValue("force", out var fr) && fr.ValueKind == JsonValueKind.True;
            var doOpen = !callArgs.TryGetValue("open", out var op) || op.ValueKind != JsonValueKind.False;
            var (bus, plan) = PackageSession(session, callArgs);
            // PreferMostUsed scans session work root if set
            var step = await ProjectOps.CreateAsync(bus, plan, outputDir, projName, template, policy, tfm, engPolicy, engines, force, cancellationToken)
                .ConfigureAwait(false);
            string? openMeta = null;
            if (doOpen && step.Ok && step.Data is { } dataEl)
            {
                string? openPath = null;
                if (dataEl.TryGetProperty("project", out var proj) && proj.GetString() is { Length: > 0 } pp)
                    openPath = pp;
                else if (dataEl.TryGetProperty("tsconfig", out var ts) && ts.GetString() is { Length: > 0 } tp)
                    openPath = tp;
                else if (dataEl.TryGetProperty("outputDir", out var od) && od.GetString() is { Length: > 0 } odir)
                    openPath = odir;
                else if (dataEl.TryGetProperty("output_dir", out var od2) && od2.GetString() is { Length: > 0 } odir2)
                    openPath = odir2;
                if (openPath is not null)
                {
                    EnsureOpenRecentWired();
                    var open = settings.Languages.Detect(openPath);
                    var park = docStore.ParkOutsideProject(open.Root);
                    openMeta = IdeLanguageTools.ApplyOpen(session, open, park);
                    shellHabitat.SyncSessionCwd(session.ProjectRoot);
                    NotifyListChanged();
                }
            }

            return IdeLanguageTools.MergeStepOpenMeta(step.ToJson(), openMeta);
        }
        case "cdp_project_list":
        {
            var (bus, plan) = PackageSession(session, callArgs);
            var root = callArgs.TryGetValue("root", out var rEl) ? rEl.GetString() : null;
            return (await ProjectOps.ListAsync(bus, plan, root, cancellationToken).ConfigureAwait(false)).ToJson();
        }
        case "cdp_project_close":
        {
            session.ProjectRoot = null;
            session.ProjectKind = null;
            session.SolutionOrProjectPath = null;
            session.TsConfigPath = null;
            session.Language = null;
            await IdeLanguageTools.CloseProjectAsync().ConfigureAwait(false);
            RoslynMcp.ServiceLayer.MsBuildWorkspaceHost.Invalidate();
            RoslynMcp.ServiceLayer.DiagnosticsResultCache.InvalidateAll();
            NotifyListChanged();
            return JsonSerializer.Serialize(new { ok = true, kind = "projects.close", summary = "session_cleared" }, Pretty);
        }
        case "cdp_project_add_to_sln":
        case "cdp_sln_add":
        {
            if (!callArgs.TryGetValue("project", out var prEl) || prEl.GetString() is not { Length: > 0 } project)
                throw new ArgumentException("project is required.");
            var solution = callArgs.TryGetValue("solution", out var sEl) ? sEl.GetString() : null;
            var inRoot = callArgs.TryGetValue("in_root", out var ir) && ir.ValueKind == JsonValueKind.True;
            var solFolder = callArgs.TryGetValue("solution_folder", out var sfEl) ? sfEl.GetString() : null;
            var (bus, plan) = PackageSession(session, callArgs);
            return (await SolutionOps.AddProjectAsync(bus, plan, project, solution, inRoot, solFolder, cancellationToken)
                .ConfigureAwait(false)).ToJson();
        }
        case "cdp_sln_create":
        {
            if (!callArgs.TryGetValue("output_dir", out var odEl) || odEl.GetString() is not { Length: > 0 } outputDir)
                throw new ArgumentException("output_dir is required.");
            var slnName = callArgs.TryGetValue("name", out var nEl) ? nEl.GetString() : null;
            var force = callArgs.TryGetValue("force", out var fr) && fr.ValueKind == JsonValueKind.True;
            var doOpen = !callArgs.TryGetValue("open", out var op) || op.ValueKind != JsonValueKind.False;
            var (bus, plan) = PackageSession(session, callArgs);
            var step = await SolutionOps.CreateAsync(bus, plan, outputDir, slnName, force, doOpen, cancellationToken)
                .ConfigureAwait(false);
            string? openMeta = null;
            if (doOpen && step.Ok && step.Data is { } dataEl)
            {
                string? openPath = null;
                if (dataEl.TryGetProperty("solution", out var sol) && sol.GetString() is { Length: > 0 } sp)
                    openPath = sp;
                else if (dataEl.TryGetProperty("output_dir", out var od) && od.GetString() is { Length: > 0 } odir)
                    openPath = odir;
                else if (dataEl.TryGetProperty("outputDir", out var od2) && od2.GetString() is { Length: > 0 } odir2)
                    openPath = odir2;
                if (openPath is not null)
                {
                    EnsureOpenRecentWired();
                    var open = settings.Languages.Detect(openPath);
                    var park = docStore.ParkOutsideProject(open.Root);
                    openMeta = IdeLanguageTools.ApplyOpen(session, open, park);
                    shellHabitat.SyncSessionCwd(session.ProjectRoot);
                    NotifyListChanged();
                }
            }

            return IdeLanguageTools.MergeStepOpenMeta(step.ToJson(), openMeta);
        }
        case "cdp_sln_list":
        {
            var (bus, plan) = PackageSession(session, callArgs);
            var root = callArgs.TryGetValue("root", out var rEl) ? rEl.GetString() : null;
            return (await SolutionOps.ListAsync(bus, plan, root, cancellationToken).ConfigureAwait(false)).ToJson();
        }
        case "cdp_sln_projects":
        {
            var (bus, plan) = PackageSession(session, callArgs);
            var solution = callArgs.TryGetValue("solution", out var sEl) ? sEl.GetString() : null;
            return (await SolutionOps.ListProjectsAsync(bus, plan, solution, cancellationToken).ConfigureAwait(false))
                .ToJson();
        }
        case "cdp_sln_remove":
        {
            if (!callArgs.TryGetValue("project", out var prEl) || prEl.GetString() is not { Length: > 0 } project)
                throw new ArgumentException("project is required.");
            var solution = callArgs.TryGetValue("solution", out var sEl) ? sEl.GetString() : null;
            var (bus, plan) = PackageSession(session, callArgs);
            return (await SolutionOps.RemoveProjectAsync(bus, plan, project, solution, cancellationToken)
                .ConfigureAwait(false)).ToJson();
        }
        case "cdp_tools":
        {
            var qPhase = session.Phase;
            var qObj = session.Object;
            CdpIntent? qIntent = session.Intent;
            string? qLang = session.Language;
            if (callArgs.TryGetValue("phase", out var p2) && CdpEnumParse.TryParsePhase(p2.GetString(), out var pp))
                qPhase = pp;
            if (callArgs.TryGetValue("object", out var o2) && CdpEnumParse.TryParseObject(o2.GetString(), out var oo))
                qObj = oo;
            if (callArgs.TryGetValue("intent", out var i2) && CdpEnumParse.TryParseIntent(i2.GetString(), out var ii))
                qIntent = ii;
            if (callArgs.TryGetValue("language", out var l2) && settings.Languages.TryNormalize(l2.GetString(), out var ll))
                qLang = CdpLanguages.IsAny(ll) ? null : ll;
            var limit = PhaseObjectCatalog.DefaultListToolsLimit;
            if (callArgs.TryGetValue("limit", out var lim) && lim.TryGetInt32(out var li))
                limit = li;
            var hits = PhaseObjectCatalog.Query(allAffordances, qPhase, qObj, qIntent, limit, qLang);
            return JsonSerializer.Serialize(new
            {
                phase = CdpEnumParse.ToWire(qPhase),
                @object = CdpEnumParse.ToWire(qObj),
                intent = qIntent is null ? null : CdpEnumParse.ToWire(qIntent.Value),
                language = qLang,
                total = hits.Count,
                tools = hits.Select(h => new
                {
                    name = h.Affordance.PrefixedName,
                    score = h.Score,
                    cost = h.Affordance.Cost,
                    risk = h.Affordance.Risk,
                    hint = h.Affordance.Hint
                })
            }, Pretty);
        }
        case "cdp_cockpit":
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureWorkspaceDb(); // desk_seats + script_last_run (WitDB)

            return await IdeCockpit.BuildAsync(
                    session,
                    docStore,
                    shellHabitat,
                    internetBrowser,
                    ideSettings,
                    mcpOutlet,
                    byDomain,
                    workspaceStore,
                    workspaceState,
                    callArgs,
                    DispatchAsync,
                    cancellationToken,
                    warm)
                .ConfigureAwait(false);
        }
        case "cdp_session":
        {
            cancellationToken.ThrowIfCancellationRequested();
            var shortlistLimit = 12;
            if (callArgs.TryGetValue("shortlist_limit", out var sl) && sl.TryGetInt32(out var sli))
                shortlistLimit = sli;
            var (wid, sid, sname, dbPath) = (null as string, null as string, null as string, workspaceDbPath);
            if (workspaceStore is not null)
                (wid, sid, sname, dbPath) = workspaceStore.PlaneIds(workspaceState);
            var workspacePlane = new WorkspacePlaneDto
            {
                ActiveIntentId = wid,
                ActiveSceneId = sid,
                ActiveSceneName = sname,
                DatabasePath = dbPath
            };
            var plane = await SessionPlane.BuildSessionAsync(
                session, modules, byDomain, allAffordances, callArgs, shortlistLimit, workspacePlane).ConfigureAwait(false);
            return JsonSerializer.Serialize(plane, Pretty);
        }
        case "cdp_work":
        {
            // Escape hatch: Cursor host may omit standalone cdp_buffer / cdp_debug from ListTools;
            // buffer_* and debug_* ops ride on already-advertised cdp_work.
            string? workOp = null;
            if (callArgs.TryGetValue("op", out var workOpEl))
            {
                workOp = workOpEl.ValueKind == JsonValueKind.String
                    ? workOpEl.GetString()
                    : workOpEl.ToString();
            }

            if (workOp is { Length: > 0 }
                && workOp.Trim().StartsWith("buffer_", StringComparison.OrdinalIgnoreCase))
            {
                var sub = workOp.Trim()["buffer_".Length..].Trim().ToLowerInvariant();
                var mapped = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (var kv in callArgs)
                    mapped[kv.Key] = kv.Value;
                mapped["op"] = JsonSerializer.SerializeToElement(sub);
                return await DocumentEditPlane
                    .DispatchAsync("cdp_buffer", docStore, session, byDomain, mapped, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (workOp is { Length: > 0 }
                && workOp.Trim().StartsWith("debug_", StringComparison.OrdinalIgnoreCase))
            {
                var sub = workOp.Trim()["debug_".Length..].Trim().ToLowerInvariant();
                var mapped = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (var kv in callArgs)
                    mapped[kv.Key] = kv.Value;
                mapped["op"] = JsonSerializer.SerializeToElement(sub);
                return await DebugPlane
                    .DispatchAsync(session, byDomain, mapped, cancellationToken)
                    .ConfigureAwait(false);
            }

            return JsonSerializer.Serialize(DispatchCdpWork(callArgs), Pretty);
        }
        case "cdp_csx_check":
        {
            var code = await ResolveCsxSourceAsync(callArgs).ConfigureAwait(false);
            var report = await ScriptHost.CheckAsync(code, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Serialize(report, Pretty);
        }
        case "cdp_csx_help":
        {
            var op = callArgs.TryGetValue("op", out var opEl) && opEl.GetString() is { Length: > 0 } ops
                ? ops.Trim()
                : "toc";
            var max = callArgs.TryGetValue("max", out var maxEl) && maxEl.ValueKind == JsonValueKind.Number
                ? maxEl.GetInt32()
                : (int?)null;
            if (op.Equals("toc", StringComparison.OrdinalIgnoreCase))
                return CsxHelpCatalog.Toc(max ?? 48);
            if (op.Equals("of", StringComparison.OrdinalIgnoreCase))
            {
                var path = callArgs.TryGetValue("path", out var pEl) ? pEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(path))
                    throw new ArgumentException("path required for cdp_csx_help op=of (e.g. Symbol or SemanticMap.Explore).");
                return CsxHelpCatalog.Of(path!, max ?? 40);
            }

            throw new ArgumentException("op must be toc|of");
        }
        case "cdp_evidence":
        {
            var kind = callArgs.TryGetValue("kind", out var kEl) && kEl.GetString() is { Length: > 0 } ks
                ? ks.Trim()
                : "auto";
            string? text = callArgs.TryGetValue("text", out var tEl) ? tEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(text)
                && callArgs.TryGetValue("path", out var epEl)
                && epEl.GetString() is { Length: > 0 } ep)
            {
                text = await File.ReadAllTextAsync(Path.GetFullPath(ep), cancellationToken).ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("text or path required for cdp_evidence");

            var ctx = new EvidenceContext(
                ProjectRoot: session.ProjectRoot,
                SolutionOrProjectPath: session.SolutionOrProjectPath);
            return EvidencePreprocess.ToJson(EvidencePreprocess.Project(kind, text, ctx));
        }
        case "cdp_csx_run":
        {
            var code = await ResolveCsxSourceAsync(callArgs).ConfigureAwait(false);
            var mode = callArgs.TryGetValue("mode", out var mEl) && mEl.GetString() is { Length: > 0 } ms
                ? ms.Trim()
                : "run";
            var dry = mode.Equals("dry_run", StringComparison.OrdinalIgnoreCase)
                      || mode.Equals("dryRun", StringComparison.OrdinalIgnoreCase);
            var root = callArgs.TryGetValue("workspace_path", out var wp) && wp.GetString() is { Length: > 0 } wps
                ? Path.GetFullPath(wps)
                : session.ProjectRoot is { Length: > 0 } pr ? pr : Environment.CurrentDirectory;
            var plan = new PlanContext
            {
                PrimaryRoot = root,
                WorkRoot = root,
                PlanId = "",
                SolutionOrProjectPath = session.SolutionOrProjectPath,
                Language = session.Language
            };
            ProjectSettingsLoader.Hydrate(plan);
            var bus = new ScriptToolBus(async (domain, underlying, args, ct) =>
            {
                if (string.Equals(domain, "cdp", StringComparison.Ordinal)
                    && string.Equals(underlying, "session_open", StringComparison.Ordinal))
                {
                    EnsureOpenRecentWired();
                    var path = args.TryGetValue("path", out var pEl) ? pEl.GetString() : null;
                    if (string.IsNullOrWhiteSpace(path))
                        throw new ArgumentException("path required for cdp.session_open");
                    var open = settings.Languages.Detect(path!);
                    var park = docStore.ParkOutsideProject(open.Root);
                    var payload = IdeLanguageTools.ApplyOpen(session, open, park);
                    // Keep Plan in sync with session for rest of this CSX.
                    plan.Rebind(
                        open.Root,
                        open.SolutionOrProjectPath ?? open.TsConfigPath,
                        CdpLanguages.IsAny(open.Language) ? null : open.Language);
                    NotifyListChanged();
                    return payload;
                }

                if (string.Equals(domain, "cdp_work", StringComparison.Ordinal))
                {
                    var mapped = new Dictionary<string, JsonElement>(args, StringComparer.Ordinal)
                    {
                        ["op"] = JsonSerializer.SerializeToElement(underlying)
                    };
                    var result = DispatchCdpWork(mapped);
                    return result is string s
                        ? s
                        : JsonSerializer.Serialize(result, Pretty);
                }

                if (!byDomain.TryGetValue(domain, out var mod))
                    throw new ArgumentException($"Backend '{domain}' not mounted.");
                return await mod.CallAsync(underlying, args).ConfigureAwait(false);
            })
            { IsDryRun = dry };
            var report = await ScriptHost.RunAsync(code, bus, plan, dry ? "dry_run" : "run", cancellationToken)
                .ConfigureAwait(false);
            return JsonSerializer.Serialize(report, Pretty);
        }
        case "cdp_csx_run_plan":
        {
            var code = await ResolveCsxSourceAsync(callArgs).ConfigureAwait(false);
            var entry = callArgs.TryGetValue("workspace_path", out var wr) && wr.GetString() is { Length: > 0 } repo
                ? repo
                : session.ProjectRoot is { Length: > 0 } pr
                    ? pr
                    : session.SolutionOrProjectPath is { Length: > 0 } sol
                        ? sol
                        : throw new ArgumentException(
                            "workspace_path or cdp_open session (ProjectRoot) is required for run_plan.");
            var focus = callArgs.TryGetValue("scope", out var sc) && sc.GetString() is { Length: > 0 } scopeArg
                ? scopeArg
                : session.ProjectRoot ?? session.SolutionOrProjectPath ?? entry;
            var policy = callArgs.TryGetValue("promote_policy", out var pp) && pp.GetString() is { Length: > 0 } pol
                ? pol
                : WorktreePlanRunner.PromoteOverlapSafe;
            var report = await WorktreePlanRunner.RunInWorktreeAsync(
                code,
                entry,
                async (domain, underlying, args, ct) =>
                {
                    if (!byDomain.TryGetValue(domain, out var mod))
                        throw new ArgumentException($"Backend '{domain}' not mounted.");
                    return await mod.CallAsync(underlying, args).ConfigureAwait(false);
                },
                cancellationToken,
                focusPath: focus,
                promotePolicy: policy).ConfigureAwait(false);
            return JsonSerializer.Serialize(report, Pretty);
        }
        case "cdp_csx_discard":
        {
            if (!callArgs.TryGetValue("plan_id", out var pid) || pid.GetString() is not { Length: > 0 } id)
                throw new ArgumentException("plan_id is required.");
            return JsonSerializer.Serialize(WorktreePlanRunner.Discard(id), Pretty);
        }
        case "cdp_csx_promote":
        {
            if (!callArgs.TryGetValue("plan_id", out var pid2) || pid2.GetString() is not { Length: > 0 } id2)
                throw new ArgumentException("plan_id is required.");
            string? policyOverride = null;
            if (callArgs.TryGetValue("promote_policy", out var ppo) && ppo.GetString() is { Length: > 0 } po)
                policyOverride = po;
            return JsonSerializer.Serialize(WorktreePlanRunner.Promote(id2, policyOverride), Pretty);
        }
        case "cdp_shell_scene":
            return shellHabitat.Scene();
        case "cdp_shell_run":
        {
            string? cmd = callArgs.TryGetValue("command", out var cmdEl) ? cmdEl.GetString() : null;
            string[]? argv = null;
            if (callArgs.TryGetValue("argv", out var argvEl) && argvEl.ValueKind == JsonValueKind.Array)
            {
                argv = argvEl.EnumerateArray()
                    .Select(e => e.GetString() ?? throw new ArgumentException("argv entries must be strings."))
                    .ToArray();
                if (argv.Length == 0)
                    argv = null;
            }

            if (argv is null && string.IsNullOrWhiteSpace(cmd))
                throw new ArgumentException("command or argv is required.");
            string? tab = callArgs.TryGetValue("tab", out var tabEl) ? tabEl.GetString() : null;
            // Cursor Shell habit: working_directory — accept as cwd alias (agent-pain sticky miss).
            string? cwd = callArgs.TryGetValue("cwd", out var cwdEl) ? cwdEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(cwd)
                && callArgs.TryGetValue("working_directory", out var wdEl))
                cwd = wdEl.GetString();
            string? shell = callArgs.TryGetValue("shell", out var shEl) ? shEl.GetString() : null;
            int? timeout = callArgs.TryGetValue("timeout_seconds", out var toEl) && toEl.TryGetInt32(out var to)
                ? to
                : IdeSettingsHabitat.EffectiveShellTimeout();
            var background = callArgs.TryGetValue("background", out var bgEl) && bgEl.ValueKind == JsonValueKind.True;
            int? codepage = callArgs.TryGetValue("codepage", out var cpEl) && cpEl.TryGetInt32(out var cp)
                ? cp
                : IdeSettingsHabitat.EffectiveShellCodepage();
            return AttachShellEvidence(
                shellHabitat.Run(ShellDefaults(session), cmd, tab, cwd, shell, timeout, background, codepage, argv),
                session);
        }
        case "cdp_shell_history":
        {
            string? tab = callArgs.TryGetValue("tab", out var tabEl) ? tabEl.GetString() : null;
            var n = callArgs.TryGetValue("n", out var nEl) && nEl.TryGetInt32(out var nn) ? nn : 20;
            return shellHabitat.History(tab, n);
        }
        case "cdp_shell_rerun":
        {
            string? tab = callArgs.TryGetValue("tab", out var tabEl) ? tabEl.GetString() : null;
            int? index = callArgs.TryGetValue("index", out var ixEl) && ixEl.TryGetInt32(out var ix)
                ? ix
                : null;
            int? timeout = callArgs.TryGetValue("timeout_seconds", out var toEl) && toEl.TryGetInt32(out var to)
                ? to
                : IdeSettingsHabitat.EffectiveShellTimeout();
            var background = callArgs.TryGetValue("background", out var bgEl) && bgEl.ValueKind == JsonValueKind.True;
            return AttachShellEvidence(
                shellHabitat.Rerun(ShellDefaults(session), tab, index, timeout, background),
                session);
        }
        case "cdp_shell_last":
        {
            string? tab = callArgs.TryGetValue("tab", out var tabEl) ? tabEl.GetString() : null;
            var maxChars = callArgs.TryGetValue("max_chars", out var mcEl) && mcEl.TryGetInt32(out var mc)
                ? mc
                : 0;
            return AttachShellEvidence(shellHabitat.Last(tab, maxChars), session);
        }
        case "cdp_shell_which":
        {
            string? tab = callArgs.TryGetValue("tab", out var tabEl) ? tabEl.GetString() : null;
            return shellHabitat.Which(tab);
        }
        case "cdp_shell_kill":
        {
            string? tab = callArgs.TryGetValue("tab", out var tabEl) ? tabEl.GetString() : null;
            return shellHabitat.Kill(tab);
        }
        case "cdp_shell_close":
        {
            string? tab = callArgs.TryGetValue("tab", out var tabEl) ? tabEl.GetString() : null;
            return shellHabitat.Close(tab);
        }
        default:
            throw new ArgumentException($"Unknown meta tool: {name}");
    }
}

TerminalMcp.Core.ShellCwdDefaults ShellDefaults(SessionContext s) => new()
{
    ProjectRoot = s.ProjectRoot,
    ScmRoot = s.ScmRoot
};

/// <summary>On failed shell result, project stdout/stderr → evidence/v0 anchors when loci exist.</summary>
string AttachShellEvidence(string json, SessionContext s)
{
    try
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var failed = (root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.False)
            || (root.TryGetProperty("exit_code", out var exEl) && exEl.TryGetInt32(out var code) && code != 0);
        if (!failed)
            return json;

        var stdout = root.TryGetProperty("stdout", out var so) ? so.GetString() ?? "" : "";
        var stderr = root.TryGetProperty("stderr", out var se) ? se.GetString() ?? "" : "";
        var text = (stdout + "\n" + stderr).Trim();
        if (text.Length == 0)
            return json;

        var evidence = EvidencePreprocess.Project(
            "shell",
            text,
            new EvidenceContext(ProjectRoot: s.ProjectRoot, SolutionOrProjectPath: s.SolutionOrProjectPath));
        if (evidence.ItemCount == 0)
            return json;

        var node = JsonNode.Parse(json)!.AsObject();
        node["evidence"] = JsonNode.Parse(EvidencePreprocess.ToJson(evidence));
        return node.ToJsonString(Pretty);
    }
    catch
    {
        return json;
    }
}

(ScriptToolBus bus, PlanContext plan) PackageSession(
    SessionContext session,
    IReadOnlyDictionary<string, JsonElement> callArgs)
{
    _ = callArgs;
    var root = session.ProjectRoot is { Length: > 0 } pr
        ? pr
        : Environment.CurrentDirectory;
    var plan = new PlanContext
    {
        PrimaryRoot = root,
        WorkRoot = root,
        PlanId = "",
        SolutionOrProjectPath = session.SolutionOrProjectPath ?? session.TsConfigPath,
        Language = session.Language
    };
    ProjectSettingsLoader.Hydrate(plan);
    var bus = new ScriptToolBus { IsDryRun = false };
    return (bus, plan);
}

string? OptionalPath(IReadOnlyDictionary<string, JsonElement> callArgs)
{
    if (callArgs.TryGetValue("path", out var p) && p.GetString() is { Length: > 0 } path)
        return path;
    if (callArgs.TryGetValue("solution_path", out var s) && s.GetString() is { Length: > 0 } sol)
        return sol;
    return null;
}

async Task<string> ResolveCsxSourceAsync(IReadOnlyDictionary<string, JsonElement> callArgs) =>
    await IdeCsxSource.ResolveAsync(
        callArgs,
        session.ProjectRoot,
        session.SolutionOrProjectPath).ConfigureAwait(false);

void NotifyListChanged()
{
    if (serverRef is null) return;
    _ = serverRef.SendNotificationAsync(
        NotificationMethods.ToolListChangedNotification,
        cancellationToken: CancellationToken.None);
}

object DispatchCdpWork(IReadOnlyDictionary<string, JsonElement> callArgs)
{
    var store = RequireWorkspace();
    if (!callArgs.TryGetValue("op", out var opEl) || opEl.GetString() is not { Length: > 0 } op)
        throw new ArgumentException("op is required for cdp_work.");
    op = op.Trim().ToLowerInvariant();

    string? Str(string key) =>
        callArgs.TryGetValue(key, out var el) && el.GetString() is { Length: > 0 } s ? s.Trim() : null;
    Guid? GuidArg(string key)
    {
        var s = Str(key);
        if (s is null) return null;
        return Guid.TryParse(s, out var g) ? g : throw new ArgumentException($"{key} must be a GUID.");
    }
    int? IntArg(string key)
    {
        if (!callArgs.TryGetValue(key, out var el) || !el.TryGetInt32(out var n)) return null;
        return n;
    }

    var sceneName = Str("name") ?? Str("scene_name");

    return op switch
    {
        "intent_upsert" => store.IntentUpsert(workspaceState, Str("title") ?? "", GuidArg("intent_id")),
        "intent_list" => store.IntentList(),
        "intent_select" => store.IntentSelect(
            workspaceState,
            GuidArg("intent_id") ?? throw new ArgumentException("intent_id is required for intent_select.")),
        "stage_upsert" => store.StageUpsert(
            workspaceState, Str("title") ?? "", GuidArg("stage_id"), GuidArg("parent_id"), sceneName),
        "stage_list" => store.StageList(workspaceState),
        "stage_set_status" => store.StageSetStatus(
            workspaceState,
            GuidArg("stage_id") ?? throw new ArgumentException("stage_id is required."),
            Str("status") ?? throw new ArgumentException("status is required.")),
        "stage_enqueue" => EnqueueStageJob(store, Str("title") ?? "", Str("job_json"), callArgs),
        "stage_get" => store.StageGet(
            GuidArg("stage_id") ?? throw new ArgumentException("stage_id is required for stage_get.")),
        "scene_park" => store.ScenePark(
            workspaceState, session,
            sceneName ?? throw new ArgumentException("name (or scene_name) is required for scene_park."),
            Str("loot"), Str("focus_path"), IntArg("focus_line"), GuidArg("bind_stage_id")),
        "scene_switch" => store.SceneSwitch(
            workspaceState, session,
            sceneName ?? throw new ArgumentException("name (or scene_name) is required for scene_switch."),
            NotifyListChanged),
        "scene_list" => store.SceneList(workspaceState),
        "status" => store.Status(workspaceState, session),
        "tasks" or "board" or "plan" or "feature" or "task" or "focus" or "done"
            or "park" or "defer" or "deferred" or "pending" or "active" or "drop" or "rm" or "delete"
            or "feature_drop" or "task_drop"
            or "criteria" or "criterion" or "criterion_list" or "criterion_add"
            or "criterion_met" or "criterion_unmet" or "criterion_waived" or "criterion_pending"
            or "criterion_status" or "criterion_drop"
            or "promote" or "promote_plan" or "ask_confirm"
            or "share" or "share_plan"
            or "confirm" or "plan_confirm" or "approved"
            or "reject" or "plan_reject" or "denied" => IdeTaskManager.Handle(
            store,
            workspaceState,
            MergeTmOp(InjectProjectRoot(callArgs, session), op)),
        "intent_delete" => store.IntentDelete(
            workspaceState,
            GuidArg("intent_id") ?? throw new ArgumentException("intent_id is required for intent_delete.")),
        "stage_delete" => store.StageDelete(
            workspaceState,
            GuidArg("stage_id") ?? throw new ArgumentException("stage_id is required for stage_delete.")),
        _ => throw new ArgumentException(
            $"Unknown cdp_work op '{op}'. Use intent_*|stage_*|criterion_*|scene_*|status|tasks|feature|task|focus|done|drop.")
    };
}

static IReadOnlyDictionary<string, JsonElement> MergeTmOp(
    IReadOnlyDictionary<string, JsonElement> callArgs,
    string op)
{
    var d = new Dictionary<string, JsonElement>(callArgs, StringComparer.Ordinal)
    {
        ["tm_op"] = JsonSerializer.SerializeToElement(op is "tasks" or "board" or "plan" or "status" ? "board" : op)
    };
    return d;
}

static IReadOnlyDictionary<string, JsonElement> InjectProjectRoot(
    IReadOnlyDictionary<string, JsonElement> callArgs,
    SessionContext session)
{
    if (callArgs.TryGetValue("project_root", out var existing)
        && existing.ValueKind == JsonValueKind.String
        && existing.GetString() is { Length: > 0 })
        return callArgs;
    if (session.ProjectRoot is not { Length: > 0 } pr)
        return callArgs;
    var d = new Dictionary<string, JsonElement>(callArgs, StringComparer.Ordinal)
    {
        ["project_root"] = JsonSerializer.SerializeToElement(pr)
    };
    return d;
}

object EnqueueStageJob(
    IntentWorkspaceStore store,
    string title,
    string? jobJson,
    IReadOnlyDictionary<string, JsonElement> callArgs)
{
    if (string.IsNullOrWhiteSpace(jobJson))
        throw new ArgumentException("job_json is required for stage_enqueue.");
    using var doc = JsonDocument.Parse(jobJson);
    var root = doc.RootElement;
    var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
    foreach (var p in root.EnumerateObject())
        dict[p.Name] = p.Value.Clone();
    if ((!dict.ContainsKey("solution_or_project_path")
         || dict["solution_or_project_path"].ValueKind != JsonValueKind.String
         || string.IsNullOrWhiteSpace(dict["solution_or_project_path"].GetString()))
        && session.SolutionOrProjectPath is { Length: > 0 } sol)
    {
        dict["solution_or_project_path"] = JsonSerializer.SerializeToElement(sol);
    }

    var enriched = JsonSerializer.Serialize(dict);
    var created = store.StageEnqueue(workspaceState, title, enriched);
    var start = true;
    if (callArgs.TryGetValue("start_job", out var sj) && sj.ValueKind == JsonValueKind.False)
        start = false;
    if (start)
    {
        using var cdoc = JsonDocument.Parse(JsonSerializer.Serialize(created));
        var stageId = cdoc.RootElement.GetProperty("stage_id").GetGuid();
        RequireJobRunner().Enqueue(stageId, enriched);
    }

    return created;
}

static object FacetCap(MemoryFacetSettings f) => new { enabled = f.Enabled, roots = f.Roots };
static object ToggleCap(MemoryToggleSettings t) => new { enabled = t.Enabled };

IdeCommandModule.Bind(DispatchAsync);

await using var stdio = new StdioServerTransport("CdpMcp");
await using var server = McpServer.Create(stdio, options);
serverRef = server;
CdpClientWorkspace.Wire(server);
Console.Error.WriteLine($"CdpMcp {mcpVersion} backends=[{string.Join(",", byDomain.Keys)}] context={CdpEnumParse.ToWire(session.Phase)}/{CdpEnumParse.ToWire(session.Object)} isolation={CdpProfile.Kind}");
await server.RunAsync();
