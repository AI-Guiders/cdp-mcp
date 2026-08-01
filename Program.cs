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
    object? warm = null) =>
    await MetaDispatch.DispatchAsync(
        new MetaDispatchDeps
        {
            Session = session,
            DocStore = docStore,
            ByDomain = byDomain,
            Modules = modules,
            AllAffordances = allAffordances,
            Settings = settings,
            McpVersion = mcpVersion,
            SoftOrganMetaNames = SoftOrganMetaNames,
            Pretty = Pretty,
            ShellHabitat = shellHabitat,
            McpOutlet = mcpOutlet,
            InternetBrowser = internetBrowser,
            IdeSettings = ideSettings,
            WorkspaceStore = workspaceStore,
            WorkspaceState = workspaceState,
            WorkspaceDbPath = workspaceDbPath,
            ServerRef = serverRef,
            NotifyListChanged = NotifyListChanged,
            EnsureOpenRecentWired = EnsureOpenRecentWired,
            EnsureWorkspaceDb = EnsureWorkspaceDb,
            BuildVisibleTools = BuildVisibleTools,
            BuildMetaTools = BuildMetaTools,
            DispatchToolAsync = DispatchAsync,
            DispatchCdpWork = DispatchCdpWork
        },
        name,
        callArgs,
        cancellationToken,
        warm).ConfigureAwait(false);

TerminalMcp.Core.ShellCwdDefaults ShellDefaults(SessionContext s) => new()
{
    ProjectRoot = s.ProjectRoot,
    ScmRoot = s.ScmRoot
};

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


IdeCommandModule.Bind(DispatchAsync);

await using var stdio = new StdioServerTransport("CdpMcp");
await using var server = McpServer.Create(stdio, options);
serverRef = server;
CdpClientWorkspace.Wire(server);
Console.Error.WriteLine($"CdpMcp {mcpVersion} backends=[{string.Join(",", byDomain.Keys)}] context={CdpEnumParse.ToWire(session.Phase)}/{CdpEnumParse.ToWire(session.Object)} isolation={CdpProfile.Kind}");
await server.RunAsync();
