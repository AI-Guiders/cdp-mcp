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
IdeIgniteArmHost.BindTaskFocus(() => workspaceState.ActiveStageId is not null);
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

List<Tool> BuildMetaTools() =>
[
    Meta("cdp_man", "[A] CDP ops manual. tool= omit for TOC; or context_budget|cdp_health|cdp_capabilities|cdp_context|cdp_tools|cdp_session|cdp_shell_*.", new
    {
        type = "object",
        properties = new { tool = new { type = "string" } }
    }),
    Meta("cdp_health", "[A] Backend health + runtime (version/exe/build_utc/pending_update). Optional explain_tool=prefixed name → why missing from shortlist.", new
    {
        type = "object",
        properties = new
        {
            explain_tool = new { type = "string", description = "Prefixed tool name to explain visibility." }
        }
    }),
    Meta("cdp_capabilities", "Mounted domains + layers.memory facets/roots + affordance counts.", new { type = "object", properties = new { } }),
    Meta("cdp_context", "[A] Get/set session phase+object(+intent[+language]). Phase change auto-applies desk layout (SA). Hold: layout_hold= or desk.layout.hold. Triggers tools/list_changed.", new
    {
        type = "object",
        properties = new
        {
            phase = new { type = "string", description = "recall|explore|clarify|plan|act|verify|handoff — also retunes desk seats unless hold" },
            @object = new { type = "string", description = "kb|code|repo|task|finding|process|issue|session" },
            intent = new { type = "string", description = "optional find|cite|change|verify|record|ship" },
            language = new { type = "string", description = "optional language id/alias from [languages] config; empty clears" },
            layout_hold = new { type = "boolean", description = "Skip phase→desk auto-layout this call (or set desk.layout.hold)" },
            get = new { type = "boolean", description = "If true, only return current context." }
        }
    }),
    Meta("cdp_open", "Open a project path: detect .sln/.csproj/tsconfig → session root+language+scm_root; list_changed. After open, git_* may omit workspace_path (defaults to scm_root). Prefer before go_to_definition. Omit path to reopen Recent[0]; or recent_index=N. Autosaves desk bookmark for cdp_restore.", new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "File or directory (.sln, .csproj, tsconfig.json, source file, or folder). Optional if recent_index set or Recent non-empty." },
            recent_index = new { type = "integer", description = "Optional 0-based Open Recent index (0 = last opened)." }
        }
    }),
    Meta("cdp_buffer", "File buffer plane: op=scene|open|create|put|take|share|read|edit|diagnostics|close|reload|keep_disk|disk_peek + comfort undo|redo|history|copy|cut|paste|clipboard|find|…. put= dump draft; share= to operator (inbox + thin chat, no body in agent); take= into agent context (rare). Instant Save. Anchors: edit_op=anchor + place=before|after|replace (default replace). Relative path= → ProjectRoot.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene|open|create|put|take|read|edit|diagnostics|close|reload|keep_disk|disk_peek|undo|redo|history|copy|cut|paste|clipboard|clipboard_clear|find|find_all|replace_all|back|forward|nav|recent_files|scratch" },
            path = new { type = "string", description = "reload|keep_disk|disk_peek: optional (omit = all drifted); find scope=project: optional subdir; find scope=external: required absolute root; otherwise file path" },
            pad = new { type = "integer", description = "disk_peek: ± context lines around first diff (default 2)" },
            doc_id = new { type = "string" },
            diagnose = new { type = "boolean", description = "open default false; create/edit default true (csharp: syntax)" },
            flush = new { type = "boolean", description = "edit/close/undo/redo/paste default true (Instant Save). false = keep dirty in memory (batch)." },
            discard = new { type = "boolean", description = "close only: with flush=false, required to drop dirty buffer without writing." },
            refresh = new { type = "boolean", description = "open: reload from disk; diagnostics: soft prefer-cache when false" },
            force = new { type = "boolean", description = "diagnostics: recompute even if version unchanged" },
            scope = new { type = "string", description = "diagnostics: syntax|project|solution; find: buffer|project|files|external (default buffer)" },
            overwrite = new { type = "boolean", description = "create: allow replace existing file" },
            allow_shrink = new { type = "boolean", description = "edit set_text: required when new body is shorter than on-disk file" },
            start_line = new { type = "integer" },
            end_line = new { type = "integer" },
            edit_op = new { type = "string", description = "edit: anchor|set_text|replace|replace_range — prefer anchor" },
            anchor = new { type = "string", description = "edit_op=anchor / copy|cut|paste: csharp [F:;M:;K:] or xml [F:;X:path;A:attr?][+K:Element]" },
            at = new { type = "string", description = "Alias of anchor" },
            text = new { type = "string", description = "edit set_text / create body / anchor text (replace=overwrite locus; place=before|after=insert body) / paste override / find query alias" },
            old_string = new { type = "string" },
            new_string = new { type = "string", description = "replace; also alias of text for anchor" },
            start_column = new { type = "integer" },
            end_column = new { type = "integer" },
            query = new { type = "string", description = "find|find_all|replace_all needle" },
            pattern = new { type = "string", description = "Alias of query (regex when regex=true)" },
            regex = new { type = "boolean", description = "find/replace_all: Use Regular Expressions (VS toggle)" },
            ignore_case = new { type = "boolean" },
            glob = new { type = "string", description = "find scope=project|external: rg --glob (e.g. *.cs); required for volume-root external" },
            max = new { type = "integer", description = "find scope=project: hit cap" },
            peek = new { type = "boolean", description = "find scope=project: auto open+peek top hit (default true)" },
            clear = new { type = "boolean", description = "clipboard: true = clear (all, or frame= one)" },
            frame = new { type = "string", description = "paste|put|clipboard: frame id cN (omit = current MRU)" },
            place = new { type = "string", description = "edit_op=anchor|paste|put: before|after|replace (anchor default replace). paste/put also sniper. CRITICAL: place=before/after inserts — does not overwrite locus." },
            sniper = new { type = "boolean", description = "paste|put: apply into edit sniper hold" },
            preserve = new { type = "boolean", description = "paste|put: keep frame after use (default true); false = burn" },
            body = new { type = "string", description = "put: alias of text= draft body" },
            content = new { type = "string", description = "put: alias of text=" },
            ext = new { type = "string", description = "scratch: file extension (default cs)" },
            check = new { type = "boolean", description = "take: default true — run available verify" },
            vision = new { type = "boolean", description = "take: opt-in ImageContent for agent (default false; use preview_path)" },
            see = new { type = "boolean", description = "take: alias of vision=" }
        },
        required = new[] { "op" }
    }),
    Meta("cdp_editor_scene", "Editor map (git_scene analogue for buffers): open buffers + loci; path=/locus=/doc_id= → context window on demand. Prefer before multi-step edits. Single edit still fine via cdp_buffer.", new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "Focus file (opens context window if buffer open)" },
            doc_id = new { type = "string" },
            locus = new { type = "string", description = "buffer:doc-N from loci[]" },
            focus = new { type = "string", description = "Alias of locus" },
            start_line = new { type = "integer" },
            end_line = new { type = "integer" },
            context_lines = new { type = "integer", description = "Max lines in context window (default 80)" }
        }
    }),
    Meta("cdp_edit_plan", "Logical edit plan. YAML preferred. Mutate: steps. Fix (Roslyn code action, document): path + fix:[IDE0005,…]. sketch=fix drafts suggested_yaml from diags. Stable diagnostic ids. Routes mutate via cdp_buffer; fix via roslyn_apply_code_action.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "draft|validate|apply (preview→validate); default draft" },
            sketch = new { type = "string", description = "draft: fix|diags — build suggested_yaml from document diagnostics" },
            include = new { type = "array", items = new { type = "string" }, description = "draft: filter candidates by path/doc_id; cold paths listed too" },
            path = new { type = "string", description = "draft sketch=fix: focus file" },
            yaml = new { type = "string", description = "Preferred: YAML list of slices (path+fix and/or steps). Alias: slices_yaml=|plan=." },
            slices_yaml = new { type = "string", description = "Alias of yaml=" },
            plan = new { type = "string", description = "Alias of yaml=" },
            slices = new
            {
                description = "JSON array [{path,fix,message,steps…}] or YAML/JSON string (prefer yaml= instead)"
            },
            resolve_anchors = new { type = "boolean", description = "validate: dry-resolve anchor wires (default true)" },
            stop_on_error = new { type = "boolean", description = "apply: stop first failing step (default true)" },
            diagnose = new { type = "boolean", description = "apply mutate: per-step diagnostics (default true)" },
            flush = new { type = "boolean", description = "apply mutate: Instant Save per step (default true)" },
            skip_validate = new { type = "boolean", description = "apply: skip pre-validate (default false)" }
        }
    }),
    Meta("cdp_edit_sniper", "Edit sniper (kj-1848): scope From/Till corridor → target outline inside → shoot via edit_plan. Prefer go=scope/target on cdp_cockpit. Hold survives until scope_clear.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scope|target|clear|status (default status)" },
            from = new { type = "string", description = "scope: Select.From anchor wire [F:;M:;S:/L:]" },
            till = new { type = "string", description = "scope: Till wire, or body|enclosing" },
            max = new { type = "integer", description = "target: max nodes (default 48)" }
        }
    }),
    Meta("cdp_debug", "Debug plane (breakpoints + DAP): op=scene|bp_add|bp_remove|bp_set|bp_list|bp_clear|launch|attach|continue|stop|stop_context|step_*|stack|variables. Session defaults after cdp_open — no hand-written breakpoints JSON.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene|bp_add|bp_remove|bp_set|bp_list|bp_clear|launch|attach|continue|stop|stop_context|step_over|step_into|step_out|stack|variables" },
            path = new { type = "string", description = "Source file for bp_add/bp_remove" },
            file_path = new { type = "string", description = "Alias of path" },
            line = new { type = "integer", description = "1-based line for bp_add/bp_remove" },
            condition = new { type = "string" },
            breakpoints = new
            {
                type = "array",
                description = "bp_set only: [{path|file_path,line,condition?}]",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        path = new { type = "string" },
                        file_path = new { type = "string" },
                        line = new { type = "integer" },
                        condition = new { type = "string" }
                    }
                }
            },
            workspace_path = new { type = "string", description = "Optional; default = session project root after cdp_open" },
            target_path = new { type = "string", description = "Optional; default = session .csproj/.sln after cdp_open" },
            process_id = new { type = "integer", description = "attach" },
            frame_index = new { type = "integer", description = "stop_context / variables" },
            fast = new { type = "boolean" },
            configuration = new { type = "string" },
            additional_arguments = new { type = "array", items = new { type = "string" } }
        },
        required = new[] { "op" }
    }),
    Meta("cdp_recent", "List Open Recent projects/solutions (agent mirror of classic IDE + CIDE anchor→solution history).", new
    {
        type = "object",
        properties = new
        {
            take = new { type = "integer", description = "Max entries (default 12)." }
        }
    }),
    Meta("cdp_restore", "Restore Previous desk after MCP kill/reload (dual-instance comfort). Reopens last project + buffer paths from disk bookmark (%LocalAppData%/cdp-mcp/desk-previous.json). Autosaved on cdp_open / buffer open. NOT full LLM chat context. op=peek|restore (default restore). Alias cockpit go=restore. Cold tools also auto-warm once/process.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "restore (default) | peek" }
        }
    }),
    Meta("cdp_deploy", "Dual-instance Deploy — runs publish-and-deploy.ps1. Hard defaults to sibling install (D:\\cdp-mcp ↔ D:\\cdp-mcp-debug) so KillRunning does not target self. Soft stages .next. mode=rollout: soft sibling→soft self→hard sibling + hard_self.argv for terminal_*. Crystal: switch seat → go=deploy (desk auto-warms). dry_run= to preview. Alias go=deploy.", new
    {
        type = "object",
        properties = new
        {
            mode = new { type = "string", description = "soft|hard|rollout (default hard; rollout=soft sibling→soft self→hard sibling)" },
            target = new { type = "string", description = "sibling|self|release|debug|path (default sibling)" },
            force = new { type = "boolean", description = "allow hard deploy onto self install (escape)" },
            dry_run = new { type = "boolean", description = "resolve policy only — no powershell" },
            script = new { type = "string", description = "optional path to publish-and-deploy.ps1" },
            use_nuget = new { type = "boolean", description = "pass -UseNuGet to aid-publish" },
            no_nudge = new { type = "boolean", description = "skip CDP_RELOAD_NUDGE bump" },
            include_raw = new { type = "boolean", description = "include stdout_tail/stderr_tail (default slim pulse+locus)" },
            include_raw_output = new { type = "boolean", description = "alias of include_raw" }
        }
    }),
    Meta("cdp_elicit", "Spike: MCP elicitation/create → host UI (path 2). op=peek (client caps) | ask (form Да/Нет/Обсудить). Proves whether Cursor advertises elicitation.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "peek|ask (default ask)" },
            message = new { type = "string", description = "ask: prompt shown to operator" },
            ask = new { type = "string", description = "alias of message" }
        }
    }),
    Meta("cdp_land", "Land via Family:navigation Anchor wire (ADR 0186). NOT Deep-Link/URI. Pass anchor=[Family:navigation;Command:open|goto|restore|show|go;…]. Nested Anchor:[…] reuses code/xml resolve. Alias go=land.", new
    {
        type = "object",
        properties = new
        {
            anchor = new { type = "string", description = "[Family:navigation;Command:…;Go:…;Anchor:[…]]" },
            at = new { type = "string", description = "Alias of anchor" },
            wire = new { type = "string", description = "Alias of anchor" }
        },
        required = new[] { "anchor" }
    }),
    Meta("cdp_cide_presentation", "Operator CIDE glass wire (instant). op=scene|get|set. set topology=(P)(F)(M) and/or tier=cockpit|compact|auto and/or pfd_primary=/mfd_primary= and/or mfd_page=SolutionExplorer → presentation-LATEST latch → CIDE live apply. Not agent cdp_settings desk; does not mutate repo workspace.toml. Alias go=cide_presentation.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene|get|set (default scene)" },
            topology = new { type = "string", description = "set: display.screens.topology e.g. (P)(F)(M)" },
            value = new { type = "string", description = "alias of topology" },
            presentation = new { type = "string", description = "alias of topology" },
            tier = new { type = "string", description = "set: display.presentation.tier auto|compact|cockpit" },
            pfd_primary = new { type = "string", description = "set: display.instruments.pfd_primary e.g. workspace_map|solution_explorer_tree" },
            mfd_primary = new { type = "string", description = "set: display.instruments.mfd_primary" },
            pfd_status_strip = new { type = "string", description = "set: display.instruments.pfd_status_strip" },
            forward_status_strip = new { type = "string", description = "set: display.instruments.forward_status_strip" },
            instruments = new { type = "string", description = "set: JSON object of instrument slot→id (merged with pfd_primary/…)" },
            mfd_page = new { type = "string", description = "set: MfdShellPage name e.g. SolutionExplorer|Chat|Terminal" },
            page = new { type = "string", description = "alias of mfd_page" }
        }
    }),
    Meta("cdp_intercom", "Dual-cockpit Intercom voice @PF/@PM. op=scene|send|ack. send to=pm body= → intercom-LATEST → CIDE Intercom. Operator @PF → unread on desk (Message for you, sir!). v0 seats: agent=PF, operator=PM. Alias go=intercom.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene|get|inbox|send|ack (default scene)" },
            to = new { type = "string", description = "send: pm|pf or @PM|@PF (default pm)" },
            from = new { type = "string", description = "send: optional seat override (default pf)" },
            body = new { type = "string", description = "send: message text" },
            message = new { type = "string", description = "send: alias of body" },
            text = new { type = "string", description = "send: alias of body" },
            id = new { type = "string", description = "ack: optional message id" }
        }
    }),
    Meta("cdp_mcp", "Agent MCP outlet (ADR 0187) — Cursor-parity control inside CDP. op=scene|presets|mount|tools|call|unmount. Mount guests (Serena/memory/…) for a task; child tools NEVER enter host ListTools. Alias go=mcp_scene|mcp_mount|…", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene (default) | presets | mount | tools | call | unmount" },
            id = new { type = "string", description = "Mount id (default=preset name)" },
            server = new { type = "string", description = "Mounted server id for tools/call/unmount" },
            preset = new { type = "string", description = "mount: memory|serena|filesystem|time|…" },
            command = new { type = "string", description = "mount: exe if not preset" },
            args = new { description = "mount: string[] argv; call: object of child tool args" },
            tool = new { type = "string", description = "call: child tool name" },
            name = new { type = "string", description = "call alias of tool; mount transport name" },
            filter = new { type = "string", description = "tools: name/description filter" },
            take = new { type = "integer", description = "tools: max (default 40)" }
        }
    }),
    Meta("cdp_browser", "Agent internet browser in CDP (ADR 0188) — lynx + Chromium UA spoof. NOT Cursor Browser. op=scene|which|open|search|dump|links|follow|back|forward|close. Search default=DDG HTML. Alias go=scene_internet_browser.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene (default) | which | open | search | dump | links | follow | back | forward | close" },
            url = new { type = "string", description = "open: https://… (http/file ok; bare host → https://)" },
            q = new { type = "string", description = "search: query text" },
            query = new { type = "string", description = "search: alias of q" },
            engine = new { type = "string", description = "search: ddg (default) | google | bing" },
            tab = new { type = "string", description = "Browser tab id (default main / active; search→search)" },
            link = new { type = "integer", description = "follow: N from op=links" },
            filter = new { type = "string", description = "links: filter urls" },
            take = new { type = "integer", description = "links: max" },
            width = new { type = "integer", description = "lynx -width (default 100)" },
            max_chars = new { type = "integer", description = "cap dump body" },
            timeout_seconds = new { type = "integer", description = "fetch timeout (default 45)" },
            useragent = new { type = "string", description = "override UA (default Chromium spoof; env CDP_BROWSER_UA)" }
        }
    }),
    Meta("cdp_settings", "Agent IDE Tools→Options (ADR 0190). op=options|page|get|set|lsp_probe|lsp_install|lsp_ensure|lsp_add. page=languages → install LSP via IDE shell. Alias go=options / lsp_ensure.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "options|page|catalog|get|set|unset|lsp_probe|lsp_install|lsp_ensure|lsp_add|which" },
            page = new { type = "string", description = "languages|internet|desk|shell|mcp|environment|process" },
            section = new { type = "string", description = "alias of page" },
            key = new { type = "string", description = "get/set: browser.search_engine | desk.default_layout | …" },
            value = new { type = "string", description = "set value" },
            id = new { type = "string", description = "lsp_*: python|go|rust|yaml|json|markdown" },
            language = new { type = "string", description = "alias of id" },
            via = new { type = "string", description = "lsp_install/ensure: npm|pip|pipx|go|rustup|scoop|winget" },
            command = new { type = "string", description = "lsp_add: executable name" },
            args = new { description = "lsp_add: string[] server args (default --stdio)" },
            writable_only = new { type = "boolean", description = "catalog: only hot user keys" }
        }
    }),
    Meta("cdp_search", "Agent-native search organ (ADR-0009). Prefer over shell/Cursor Grep. Axes: what=text|index|symbol, where=buffer|project|external|dirty|buffers (+roots[]/path=), shape=slim|list|raw. op=run|refine|last|clear. Alias go=find_desk.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "run|refine|last|clear (default run)" },
            what = new { type = "string", description = "text (default) | index | symbol" },
            where = new { type = "string", description = "project|external|dirty|buffers|buffer" },
            shape = new { type = "string", description = "slim (default) | list | raw" },
            query = new { type = "string", description = "needle (aliases text= pattern= q=)" },
            path = new { type = "string", description = "subdir or absolute (external requires rooted)" },
            roots = new { description = "string[] multi-root / file list" },
            exclude = new { description = "refine: string[] path substrings to drop" },
            glob = new { type = "string", description = "rg --glob" },
            regex = new { type = "boolean" },
            ignore_case = new { type = "boolean" },
            max = new { type = "integer" },
            peek = new { type = "boolean", description = "auto land top hit (default true)" },
            only_dirty = new { type = "boolean", description = "where=buffers: only Dirty buffers" }
        }
    }),
    Meta("cdp_sa", "Agent-native code SA before refactor (ADR-0010). Fuse gates+dirty+clones → verdict leave|touch|split|need_more. Axes: locus/path/line, scope=file|buffer|dirty|project, depth=pulse|slim|full. Alias go=sa_desk (NOT go=sa EICAS).", new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "file locus (opens buffer for gates)" },
            locus = new { type = "string", description = "alias of path= or seat locus id" },
            anchor = new { type = "string", description = "[F:;L:;C:] wire" },
            line = new { type = "integer", description = "for find_usages next" },
            column = new { type = "integer" },
            scope = new { type = "string", description = "file|buffer|dirty|project" },
            depth = new { type = "string", description = "pulse|slim (default)|full" }
        }
    }),
    Meta("cdp_refactor", "Refactor decide desk — debt map + before/after budget + blast next + partials seam. op=plan|debt|budget|blast|partials|pulse. After sa_desk; Alias go=refactor_plan.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "plan (default)|debt|budget|blast|partials|pulse" },
            path = new { type = "string", description = "file locus" },
            scope = new { type = "string", description = "file|buffer|project" },
            line = new { type = "integer", description = "blast: find_usages line" },
            column = new { type = "integer" },
            topic = new { type = "string", description = "partials: suggested TypeName.Topic.cs" },
            after_lines = new { type = "integer", description = "budget what-if file lines" },
            extract_lines = new { type = "integer", description = "budget: before - N" },
            after_method_lines = new { type = "integer", description = "budget what-if worst method" }
        }
    }),
    Meta("cdp_debug_sa", "Agent-native Debug-SA (ADR-0011). Fuse DAP+bp+launch → verdict idle|continue|step|fix_bp|stop_rebuild|attach|need_more. Axes: scope=session|bp|stop, depth=pulse|slim|full. Alias go=debug_desk (NOT go=debug raw scene; NOT go=sa EICAS).", new
    {
        type = "object",
        properties = new
        {
            scope = new { type = "string", description = "session (default) | bp | stop" },
            depth = new { type = "string", description = "pulse|slim (default)|full" }
        }
    }),
    Meta("cdp_test_sa", "Agent-native Test-SA (ADR-0012). Fuse last_run → verdict need_more|discover|run|retest|green. Axes: scope=session|failed|last, depth=pulse|slim|full. Alias go=test_desk (NOT go=test/test_scene raw).", new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "optional .sln/.csproj override" },
            scope = new { type = "string", description = "session (default) | failed | last" },
            depth = new { type = "string", description = "pulse|slim (default)|full" }
        }
    }),
    Meta("cdp_build_sa", "Agent-native Build-Ship-SA (ADR-0013). Fuse DAP+dirty+ahead → verdict need_more|stop_rebuild|build|preflight|ship|push|clean. Axes: scope=session|build|ship, depth=pulse|slim|full. Alias go=build_desk (NOT go=build actuator; NOT go=ship take).", new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "optional .sln/.csproj override" },
            scope = new { type = "string", description = "session (default) | build | ship" },
            depth = new { type = "string", description = "pulse|slim (default)|full" }
        }
    }),
    Meta("cdp_crm", "CRM callout panel (ADR-0014). Closed codes: approved|stabilized|go_around|hold|unable|negative|say_again|continue|roger|wilco. op=scene|call|respond|last|clear|lexicon. Alias go=crm. Operator act → SSOT; agent reads pulse — no reject essays in chat.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene|call|respond|last|clear|lexicon" },
            code = new { type = "string", description = "respond: approved|stabilized|go_around|hold|…" },
            ask = new { type = "string", description = "call: what operator should answer" },
            kind = new { type = "string", description = "call: general|plan|…" },
            ref_id = new { type = "string", description = "call: correlation id" },
            why = new { type = "string", description = "respond: short code ≤80 chars, not essay" }
        }
    }),
    Meta("cdp_arch", "Architecture staging board (ADR 0196) — ontological kneeboard, not Miro. Roles CCU|Channel|CDS|Compositor|Surface + candidates as CodeAnchor wires [F:;M:;K:]. op=scene|add_role|add_candidates|elect|reject|edge|promote|clear|roles|as_built. Alias go=arch_desk|board. Board ≠ code until promote (plan-only v0).",
        new
        {
            type = "object",
            properties = new
            {
                op = new { type = "string", description = "scene|add_role|add_candidates|elect|reject|edge|promote|clear|roles|as_built" },
                role = new { type = "string", description = "ccu|channel|cds|ids|compositor|surface|instrument|databus|dal|transport" },
                role_id = new { type = "string", description = "optional stable id for the role slot" },
                id = new { type = "string", description = "alias of role_id" },
                anchors = new { type = "string", description = "CodeAnchor wires [F:;M:;K:] (array or comma-separated); not bare paths" },
                candidates = new { type = "string", description = "alias of anchors" },
                candidate = new { type = "string", description = "elect/reject: candidate id|label|wire" },
                from = new { type = "string", description = "edge: from role id|kind" },
                to = new { type = "string", description = "edge: to role id|kind" },
                kind = new { type = "string", description = "edge: feeds|mounts|projects|wires; or alias of role on add_role" },
                note = new { type = "string", description = "optional why on role" },
                view = new { type = "string", description = "scene: plan|as_built" },
                profile = new { type = "string", description = "as_built: cide|cdp_desk|unknown — overrides auto-detect" }
            }
        }),
    Meta("cdp_onboard", "Cold-start explore/onboard desk — no ADR required. Scans open ProjectRoot for entrypoints, top folders, verticals, docs presence + next[]. Not a VS Code Map. op=scene|scan|clear. Alias go=onboard_desk|explore_desk. layout=onboard|explore → M seat.",
        new
        {
            type = "object",
            properties = new
            {
                op = new { type = "string", description = "scene|scan|clear" }
            }
        }),
    Meta("cdp_toolchain", "Toolchain ensure (ADR 0198) — runtime/compiler/SDK on PATH; DAL-adjacent; NOT lsp_ensure. Any id: python|gcc|javac|go|+custom. op=scene|probe|ensure|install|add|which. Alias go=toolchain|toolchain_ensure.",
        new
        {
            type = "object",
            properties = new
            {
                op = new { type = "string", description = "scene|probe|ensure|install|add|which" },
                id = new { type = "string", description = "python|gcc|javac|go|custom" },
                via = new { type = "string", description = "winget|scoop|..." },
                bins = new { type = "string", description = "add: comma bins" },
                pairs_lsp = new { type = "string", description = "optional lsp id after ensure" }
            }
        }),
    Meta("cdp_md_author", "Markdown authoring INCLUDE organ (CIDE ADR 0023). op=scene|check|expand|export. Syntax {{ INCLUDE: rel/path }}. scope=all (default) or fence (CIDE preview parity). Alias go=md_author.",
        new
        {
            type = "object",
            properties = new
            {
                op = new { type = "string", description = "scene|check|expand|export" },
                path = new { type = "string", description = "source .md (absolute or project-relative)" },
                @out = new { type = "string", description = "export: output path (default {name}.expanded.md)" },
                scope = new { type = "string", description = "all|fence" },
                max_depth = new { type = "integer", description = "INCLUDE nest limit (default 5)" },
                max_chars = new { type = "integer", description = "expand/export body cap in response" }
            }
        }),
    Meta("cdp_learn", "Lean dialogue learning desk — stash findings so compaction cannot eat them. op=scene|stash|list|recall|promote. Journal under ws state; promote → agent-notes work/projects/_learn (or path=). Alias go=learn. Not findings (file memos) and not TM.",
        new
        {
            type = "object",
            properties = new
            {
                op = new { type = "string", description = "scene|stash|list|recall|promote" },
                title = new { type = "string", description = "stash: short card title" },
                body = new { type = "string", description = "stash: concentrated learning (required)" },
                text = new { type = "string", description = "alias of body=" },
                topic = new { type = "string", description = "optional topic tag" },
                tags = new { type = "string", description = "comma/semicolon tags" },
                id = new { type = "string", description = "recall/promote: card id (or latest)" },
                path = new { type = "string", description = "promote: knowledge-relative path (default work/projects/_learn/{id}.md)" },
                limit = new { type = "integer", description = "list: max cards (default 20)" }
            }
        }),
    Meta("cdp_files", "Agent-native File Manager (ADR-0016). Utility — not project-bound. where=cwd|project|external (+path=). op=scene|list|cd|up|stat|tree|open|text|search|roots|clear. text= lynx-like dump (pandoc/pdftotext). shape=slim|list. Alias go=files_desk. Prefer over shell ls/dir. Search facet → find_desk.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene|list|cd|up|stat|tree|open|text|search|roots|clear" },
            where = new { type = "string", description = "cwd|project|external" },
            path = new { type = "string", description = "absolute (external) or relative to cwd" },
            name = new { type = "string", description = "cd/open/stat relative name" },
            filter = new { type = "string", description = "glob or substring" },
            kind = new { type = "string", description = "all|file|dir" },
            shape = new { type = "string", description = "slim (default)|list|raw" },
            depth = new { type = "integer", description = "tree depth 1..4" },
            query = new { type = "string", description = "search facet → find_desk" },
            hidden = new { type = "boolean", description = "include hidden entries" },
            max_chars = new { type = "integer", description = "text: dump cap (default 12000)" },
            @as = new { type = "string", description = "open: buffer|edit to force buffer for docs (default text for pdf/docx/…)" }
        }
    }),
    Meta("cdp_ignite", "AutoIgnition via Chrome DevTools (CDT) into Cursor Composer — not Cognitive CDP. Requires Cursor --remote-debugging-port=9222. op=scene|probe|chats|send|arm|disarm|list|hygiene|plateau|continuity|resume. ARM: when=build_finished|test_finished|shell_finished|timer task= (TM label only). Default charge=minimal: canonical wake text + amnesia/compaction postfix at fire (no TM body in composer). charge=custom only for legacy templates. last_once=/await_operator: fire once → awaiting latch. Alias go=ignite_desk.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene|probe|chats|send|arm|disarm|list" },
            message = new { type = "string", description = "send: optional override; arm: ignored unless charge=custom" },
            task = new { type = "string", description = "arm: Task Manager label (SSOT); not injected into composer" },
            charge = new { type = "string", description = "arm: minimal (default)|custom|legacy — minimal fires canonical+amnesia postfix" },
            when = new { type = "string", description = "arm: build_finished|test_finished|shell_finished|timer" },
            @event = new { type = "string", description = "alias of when=" },
            @in = new { type = "string", description = "arm timer: 30s|5m|2h" },
            chat = new { type = "string", description = "optional chat title substring" },
            id = new { type = "string", description = "disarm id= / arm custom id" },
            all = new { type = "boolean", description = "disarm all=true" },
            ok_only = new { type = "boolean", description = "arm: fire only on green build/test (default true)" },
            settle_seconds = new { type = "integer", description = "arm: delay before CDT send after event (default 8)" },
            port = new { type = "integer", description = "CDT port (default 9222)" },
            wait_seconds = new { type = "integer", description = "max wait for idle (not Stop/Queue), default 90" }
        }
    }),
    Meta("cdp_webcam", "Sense desk — in-proc Shared+OpenCv+NAudio+Whisper. op=scene|frame|burst|av|screen|audio|transcribe|ocr|analyze. av: concurrent cam+mic (capture_av_burst parity). Alias go=webcam_desk.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene|frame|burst|av|screen|audio|transcribe|ocr|analyze" },
            camera_index = new { type = "integer", description = "default 0" },
            file_name = new { type = "string", description = "output base name without extension" },
            workspace_path = new { type = "string", description = "override; default = session project root" },
            width = new { type = "integer" },
            height = new { type = "integer" },
            jpeg_quality = new { type = "integer" },
            duration_sec = new { type = "integer", description = "burst/av/screen/audio: seconds" },
            target_fps = new { type = "integer", description = "burst/av/screen: fps" },
            burst_name = new { type = "string", description = "burst/screen/av: folder/session name" },
            session_name = new { type = "string", description = "av: session folder name" },
            save_video = new { type = "boolean", description = "av: write video.mp4 (default true)" },
            output_subdir = new { type = "string", description = "relative output dir" },
            sample_rate = new { type = "integer", description = "audio/av: Hz (default 16000)" },
            channels = new { type = "integer", description = "audio/av: 1|2 (default 1)" },
            device_number = new { type = "integer", description = "audio/av: WaveIn device index" },
            audio_path = new { type = "string", description = "transcribe: wav/webm under workspace" },
            model_path = new { type = "string", description = "transcribe: ggml model; default WHISPER_MODEL_PATH" },
            language = new { type = "string", description = "transcribe: whisper language or auto" },
            max_segments = new { type = "integer", description = "transcribe: segment cap" },
            images_dir = new { type = "string", description = "ocr: folder of images" },
            file_path = new { type = "string", description = "ocr/transcribe: single file path" },
            lang = new { type = "string", description = "ocr: tesseract langs; transcribe alias of language=" },
            sample_every = new { type = "integer", description = "ocr/analyze: every N-th file" },
            max_images = new { type = "integer", description = "ocr: cap" },
            max_frames = new { type = "integer", description = "analyze: cap" },
            burst_dir = new { type = "string", description = "analyze: folder of frames" },
            scene_cut_threshold = new { type = "number", description = "analyze: motion cut threshold 0..255" },
            output_json_path = new { type = "string", description = "ocr: write JSON path under workspace" }
        }
    }),
    Meta("cdp_pressure", "L1 pre-compact prep desk. On pressure notify (~2–3 turns before host summarization): op=arm → checklist → op=stash body=. Must axes: AutoIgnition re-ARM, Task Manager, CDP habitat. Alias go=pressure_desk|pressure. Does not offer export ritual to operator.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene|arm|stash|clear|disarm|recall" },
            body = new { type = "string", description = "stash: markdown/text — goal, decisions, open, next, ignite, plan" },
            text = new { type = "string", description = "alias of body=" },
            why = new { type = "string", description = "arm: reason (default L1 pressure notify)" },
            ignite = new { type = "string", description = "stash: AutoIgnition note" },
            plan = new { type = "string", description = "stash: Task Manager focus note" }
        }
    }),
    Meta("cdp_icm", "ICM discovery for on-demand GUI CDP client (ADR-0019). op=scene|aliases|resolve|invoke. Melody command_id → CDP tool via IdeCommandAliasMap; invoke uses ExecuteAliasedAsync. Alias go=icm|icm_desk. Does not mutate IntentMelody.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene|aliases|resolve|invoke" },
            command_id = new { type = "string", description = "resolve|invoke: Melody or CDP command_id" },
            id = new { type = "string", description = "alias of command_id" },
            command = new { type = "string", description = "alias of command_id" }
        }
    }),
    Meta("cdp_cockpit_host", "Anchor Start/Stop — operator GUI cockpit host. op=scene|start|stop. Config: [cockpit_host] exe in cdp-mcp.toml; path= overrides once; CDP_COCKPIT_HOST_EXE env is escape only. Stop kills host pid only (MCP stays). Alias go=cockpit_start|cockpit_stop|cockpit_host. Does not mutate Melody/settings.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene|start|stop" },
            path = new { type = "string", description = "start: exe path override" },
            exe = new { type = "string", description = "alias of path" },
            args = new { type = "string", description = "start: process arguments" }
        }
    }),
    Meta("cdp_build", "IDE Build: session project after cdp_open. Harness picks projection (csharp→dotnet / typescript→npm|tsc). Prefer over shell. Default detail=auto: green→pulse; fail→errors[].",
    new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "Optional .sln/.csproj/tsconfig; default = session after cdp_open" },
            solution_path = new { type = "string", description = "Alias of path (csharp)" },
            configuration = new { type = "string", description = "Debug|Release (csharp)" },
            framework = new { type = "string" },
            no_restore = new { type = "boolean" },
            detail = new { type = "string", description = "auto (default: green→pulse, fail→slim) | pulse | slim | full" },
            include_raw_output = new { type = "boolean", description = "forces detail=full" },
            timeout_seconds = new { type = "integer" }
        }
    }),
    Meta("cdp_run", "IDE Run: session project. csharp→dotnet run; typescript→npm start|dev. Prefer over shell.", new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "Optional project path; default = session" },
            configuration = new { type = "string" },
            no_build = new { type = "boolean", description = "Pass --no-build (csharp)" },
            timeout_seconds = new { type = "integer", description = "Default 120" },
            additional_arguments = new
            {
                type = "array",
                items = new { type = "string" },
                description = "Args after -- passed to the app (csharp)"
            }
        }
    }),
    Meta("cdp_test", "IDE Test: session project. csharp→dotnet test; typescript→npm test. Prefer over shell. Prefer cdp_test_scene first. Default detail=auto: green→pulse only; fail→failed_tests[].",
    new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "Optional .sln/.csproj/package root; default = session" },
            solution_path = new { type = "string", description = "Alias of path (csharp)" },
            configuration = new { type = "string" },
            filter = new { type = "string", description = "VSTest --filter" },
            detail = new { type = "string", description = "auto (default: green→pulse, fail→slim) | pulse | slim | full" },
            include_raw_output = new { type = "boolean", description = "forces detail=full" },
            timeout_seconds = new { type = "integer" }
        }
    }),
    Meta("cdp_test_scene", "Test Runner map (git_scene analogue): discover FQNs via `dotnet test --list-tests` + last_run cache. Prefer before cdp_test / shell archaeology.", new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string" },
            solution_path = new { type = "string" },
            configuration = new { type = "string" },
            max_tests = new { type = "integer", description = "Cap discovered FQNs (default 500)" },
            timeout_seconds = new { type = "integer" }
        }
    }),
    Meta("cdp_goto", "VS Ctrl+T Go To All + Ctrl+Q features: fuzzy files/types/members → anchors (top hit auto open+peek). Prefixes: f/t/m/# code; q: desk verbs (or kind=feature). Prefer over find when you know the name.", new
    {
        type = "object",
        properties = new
        {
            query = new { type = "string", description = "Search text; optional prefix 't Foo' / 'f:Bar' / 'q undo'" },
            q = new { type = "string", description = "Alias of query" },
            kind = new { type = "string", description = "all|file|type|member|symbol|feature (default all)" },
            filter = new { type = "string", description = "Alias of kind" },
            max = new { type = "integer", description = "Cap hits (default 40)" },
            peek = new { type = "boolean", description = "Auto open+peek top code hit (default true)" }
        }
    }),
    Meta("cdp_analysis_scene", "Code Analysis domain scene (git_scene/test_scene peer). On demand — not MFD. feature omit → map; correspondence → ADR/docs↔code (+ doc_body reverse, unified context=); semantic_map → related neighbors; clones → VS-style duplicates. path=/anchor=; mode= for semantic_map.", new
    {
        type = "object",
        properties = new
        {
            feature = new { type = "string", description = "omit|scene → map; correspondence|semantic_map|clones" },
            op = new { type = "string", description = "Alias of feature" },
            scope = new { type = "string", description = "clones: file|method|selection|project|solution" },
            path = new { type = "string", description = "file under analysis" },
            anchor = new { type = "string", description = "seed wire [F:;M:;L:]" },
            from = new { type = "string", description = "Alias of anchor" },
            mode = new { type = "string", description = "semantic_map: related|subgraph|… (default related)" },
            preset = new { type = "string", description = "semantic_map: navigation preset id" },
            max_related = new { type = "integer", description = "semantic_map hit cap" },
            line = new { type = "integer", description = "semantic_map optional line" },
            column = new { type = "integer", description = "semantic_map optional column" },
            search_in = new { type = "string", description = "When seed set: file|project|solution (default project if open)" },
            min_statements = new { type = "integer", description = "Default 10 project/solution, 3 local" },
            max_files = new { type = "integer" },
            max_groups = new { type = "integer" },
            start_line = new { type = "integer" },
            end_line = new { type = "integer" }
        }
    }),
    Meta("cdp_script_scene", "Script habitat (put → buffer diagnostics → check → run → report). Not external throwaway CSX. op omit → map; put name=/text= under .cdp/scripts; open|check|run|last|help. Run wraps cdp_csx_run; check = ScriptHost allowlist + buffer diags.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "omit|scene|put|open|check|run|last|help" },
            feature = new { type = "string", description = "Alias of op" },
            name = new { type = "string", description = "put/open/check/run: script file name under .cdp/scripts" },
            path = new { type = "string", description = "script path (absolute or project-relative)" },
            file = new { type = "string", description = "Alias of path/name" },
            text = new { type = "string", description = "put: draft body" },
            body = new { type = "string", description = "put: alias of text" },
            code = new { type = "string", description = "put: alias of text" },
            overwrite = new { type = "boolean", description = "put: replace existing (default true if exists)" },
            mode = new { type = "string", description = "run: run|dry_run" },
            refresh = new { type = "boolean", description = "open: reload from disk" }
        }
    }),
    Meta("cdp_ps1_scene", "PowerShell ISE-analogue habitat (put → buffer → AST check → pwsh -File run → last). Scripts under .cdp/ps1/*.ps1. op omit → map; put|open|check|run|last|help. dry_run = AST-only.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "omit|scene|put|open|check|run|last|help" },
            feature = new { type = "string", description = "Alias of op" },
            name = new { type = "string", description = "put/open/check/run: file name under .cdp/ps1" },
            path = new { type = "string", description = "script path (absolute or project-relative)" },
            file = new { type = "string", description = "Alias of path/name" },
            text = new { type = "string", description = "put: draft body" },
            body = new { type = "string", description = "put: alias of text" },
            code = new { type = "string", description = "put: alias of text" },
            overwrite = new { type = "boolean", description = "put: replace existing" },
            mode = new { type = "string", description = "run: run|dry_run (dry_run=AST check)" },
            refresh = new { type = "boolean", description = "open: reload from disk" },
            timeout_seconds = new { type = "integer", description = "run: process timeout (default 120)" }
        }
    }),
    Meta("cdp_test_plan", "Select tests then preview|apply. include[] FQNs, failed_first=true (from last_run), or filter=. op=preview|apply → structured test_run/v0 + evidence.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "preview|apply (aliases draft|run); default preview" },
            path = new { type = "string" },
            solution_path = new { type = "string" },
            include = new { type = "array", items = new { type = "string" }, description = "FQNs or filter fragments" },
            failed_first = new { type = "boolean", description = "Re-run last_run failed set" },
            filter = new { type = "string", description = "Raw VSTest --filter" },
            configuration = new { type = "string" },
            detail = new { type = "string", description = "auto|pulse|slim|full (same as cdp_test)" },
            include_raw_output = new { type = "boolean" },
            timeout_seconds = new { type = "integer" }
        }
    }),
    Meta("cdp_pkg_find", "Search packages (NuGet or npm by session language). Prefer over shell.", new
    {
        type = "object",
        properties = new
        {
            query = new { type = "string" },
            take = new { type = "integer", description = "Default 5, max 25" }
        },
        required = new[] { "query" }
    }),
    Meta("cdp_pkg_list", "List dependencies of session project.", new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "Optional .csproj / package root override" }
        }
    }),
    Meta("cdp_pkg_add", "Add package reference (dotnet add / npm install) to session project.", new
    {
        type = "object",
        properties = new
        {
            id = new { type = "string", description = "Package id" },
            version = new { type = "string", description = "Optional version; omit = latest stable tooling default" },
            path = new { type = "string", description = "Optional .csproj / package root (needed if session is .sln)" }
        },
        required = new[] { "id" }
    }),
    Meta("cdp_pkg_remove", "Remove package reference from session project.", new
    {
        type = "object",
        properties = new
        {
            id = new { type = "string" },
            path = new { type = "string" }
        },
        required = new[] { "id" }
    }),
    Meta("cdp_pkg_update", "Update package (NuGet: add version; npm: update/install @ver).", new
    {
        type = "object",
        properties = new
        {
            id = new { type = "string" },
            version = new { type = "string" },
            path = new { type = "string" }
        },
        required = new[] { "id" }
    }),
    Meta("cdp_pkg_outdated", "List outdated packages for session project.", new
    {
        type = "object",
        properties = new { path = new { type = "string" } }
    }),
    Meta("cdp_project_scene", "Project map before create (git_scene analogue): curated templates (VS-like), session anchors, existing csproj/sln. Optional include_installed=true → dotnet new list. Prefer before inventing files / guessing template=.", new
    {
        type = "object",
        properties = new
        {
            root = new { type = "string", description = "Scan root (default session work root)" },
            include_installed = new { type = "boolean", description = "Also parse `dotnet new list --type project` (capped)" },
            max_existing = new { type = "integer", description = "Cap for existing projects (default 40)" },
            max_installed = new { type = "integer", description = "Cap for installed templates (default 80)" }
        }
    }),
    Meta("cdp_project_create", "Scaffold project. csharp: tfm_policy (+ tfm=). typescript: engine_policy (+ engines=). Policies prefer_most_used|latest|lts|specified — LTS from vendor meta (dotnet releases-index release-type / node dist lts). Optional open=true. Prefer cdp_project_scene first.", new
    {
        type = "object",
        properties = new
        {
            output_dir = new { type = "string", description = "Directory for the new project" },
            name = new { type = "string", description = "Project name (default = folder name)" },
            template = new { type = "string", description = "dotnet new template id (default console)" },
            tfm_policy = new { type = "string", description = "prefer_most_used|latest|lts|specified (default prefer_most_used)" },
            tfm = new { type = "string", description = "Required when tfm_policy=specified (e.g. net10.0)" },
            engine_policy = new { type = "string", description = "typescript: prefer_most_used|latest|lts|specified" },
            engines = new { type = "string", description = "Required when engine_policy=specified (e.g. >=20)" },
            force = new { type = "boolean" },
            open = new { type = "boolean", description = "cdp_open the created project into session (default true)" }
        },
        required = new[] { "output_dir" }
    }),
    Meta("cdp_project_list", "List .csproj / tsconfig under root (default session project root).", new
    {
        type = "object",
        properties = new { root = new { type = "string" } }
    }),
    Meta("cdp_project_close", "Clear session project anchor (language/root/solution/tsconfig).", new
    {
        type = "object",
        properties = new { }
    }),
    Meta("cdp_project_add_to_sln", "Add csproj to solution (session .sln/.slnx or solution=). Alias of cdp_sln_add.", new
    {
        type = "object",
        properties = new
        {
            project = new { type = "string", description = "Path to .csproj" },
            solution = new { type = "string", description = "Path to .sln/.slnx (default: session or unique in work root)" },
            in_root = new { type = "boolean", description = "dotnet sln add --in-root" },
            solution_folder = new { type = "string", description = "dotnet sln add --solution-folder" }
        },
        required = new[] { "project" }
    }),
    Meta("cdp_sln_create", "Scaffold solution (dotnet new sln). Optional open=true into session.", new
    {
        type = "object",
        properties = new
        {
            output_dir = new { type = "string" },
            name = new { type = "string" },
            force = new { type = "boolean" },
            open = new { type = "boolean", description = "cdp_open the created .sln/.slnx (default true)" }
        },
        required = new[] { "output_dir" }
    }),
    Meta("cdp_sln_list", "List .sln / .slnx under root (default session work root).", new
    {
        type = "object",
        properties = new { root = new { type = "string" } }
    }),
    Meta("cdp_sln_projects", "List projects in a solution (dotnet sln list).", new
    {
        type = "object",
        properties = new { solution = new { type = "string" } }
    }),
    Meta("cdp_sln_add", "Add project to solution (dotnet sln add).", new
    {
        type = "object",
        properties = new
        {
            project = new { type = "string" },
            solution = new { type = "string" },
            in_root = new { type = "boolean" },
            solution_folder = new { type = "string" }
        },
        required = new[] { "project" }
    }),
    Meta("cdp_sln_remove", "Remove project from solution (dotnet sln remove).", new
    {
        type = "object",
        properties = new
        {
            project = new { type = "string" },
            solution = new { type = "string" }
        },
        required = new[] { "project" }
    }),
    Meta("cdp_tools", "[A] Shortlist catalog=f(phase,object[,intent][,language]) — agent command palette preview.", new
    {
        type = "object",
        properties = new
        {
            phase = new { type = "string" },
            @object = new { type = "string" },
            intent = new { type = "string" },
            language = new { type = "string" },
            limit = new { type = "integer" }
        }
    }),
    Meta("cdp_cockpit", "[A] Agent IDE desk — Scan Pattern seats + view once (ADR 0191/0193). Slim alert=sa pulse (sit/locus/layout). [C] go_detail=full|pane_full=. [W] seats_detail=full spray. World channel replaces on M. Cold auto-restore.", new
    {
        type = "object",
        properties = new
        {
            mfd = new { type = "string", description = "Legacy alias: nav→desk_detail=nav; sys|chk|gates→soft organs (same as go=). Prefer go=sys|chk|gates. Alias: page=." },
            page = new { type = "string", description = "Alias of mfd." },
            locus = new { type = "string", description = "Focus locus id from loci[] (e.g. git:scm, shell:main, buffer:doc-1, browser:net)." },
            focus = new { type = "string", description = "Alias of locus." },
            go = new { type = "string", description = "Desk verb → organ; in seats mode places into P|F|M by policy. Alias: do=." },
            @do = new { type = "string", description = "Alias of go." },
            cmd = new { type = "string", description = "REPL line: \"go browser\" | \"layout cockpit\" | \"seat m git\" | \"clear\". Alias: line=|repl=." },
            line = new { type = "string", description = "Alias of cmd." },
            repl = new { type = "string", description = "Alias of cmd." },
            go_args = new { type = "object", description = "Optional args merged into the target organ tool." },
            go_detail = new { type = "string", description = "[A] pulse (default) | [C] full (organ dump in go.result)." },
            layout = new { type = "string", description = "Seat preset: cockpit | code+net | code+shell | code+git | desk. Sticky replace-in-seat." },
            seat = new { type = "string", description = "Explicit seat: p|forward|m (with organ=)." },
            organ = new { type = "string", description = "Organ pin for seat= (or pin=)." },
            pins = new { description = "Seats mode: scan-order fill P,F,M. Tiles mode: sticky pin list." },
            tiles = new { description = "Alias of pins." },
            pin = new { description = "Tiles mode: add pin(s). Seats: prefer seat=+organ=." },
            pin_clear = new { type = "boolean", description = "Clear seats/pins." },
            clear_pins = new { type = "boolean", description = "Alias of pin_clear." },
            seat_clear = new { type = "boolean", description = "Alias of pin_clear (seats)." },
            pane_full = new { type = "string", description = "[C] Which seat/pin gets go_detail=full (also forces panes)." },
            full_pane = new { type = "string", description = "Alias of pane_full." },
            seats_detail = new { type = "string", description = "[A] compact (default: view+slots). [W] full alone is refused — use pane_full=<seat|organ> for one dump." },
            view_detail = new { type = "string", description = "Alias of seats_detail." },
            desk_detail = new { type = "string", description = "slim (default: omit loci[]/go_verbs[]) | nav | full. Alias: nav_detail=." },
            nav_detail = new { type = "string", description = "Alias of desk_detail." },
            include_submodules = new { type = "boolean", description = "Pass through to git_scene (default false)." },
            no_restore = new { type = "boolean", description = "Skip once-per-process cold auto desk bookmark restore (default false)." }
        }
    }),
    Meta("cdp_session", "[A] Session plane: context + shortlist + health + continuity (pack omitted by default). [C/W] include_pack=true embeds definitions/process/procedure dogfood.", new
    {
        type = "object",
        properties = new
        {
            explain_tool = new { type = "string", description = "Optional: why this tool is hidden/visible." },
            include_debug = new { type = "boolean", description = "[C] Include debug_stop_context when debug mounted (default true)." },
            include_pack = new { type = "boolean", description = "[C/W] Embed LLM-native pack process+procedure+debug-radius (default false = A)." },
            pack_id = new { type = "string", description = "Pack id when include_pack=true (default epistemic-scene)." },
            process_id = new { type = "string", description = "Process id when include_pack=true (default bug-radius-shrink)." },
            procedure_id = new { type = "string", description = "Optional when-card id when include_pack=true." },
            shortlist_limit = new { type = "integer", description = "Shortlist size in snapshot (default 12)." }
        }
    }),
    Meta("cdp_work", "Intent workspace + buffer + debug escape. op=intent_*|stage_*|scene_*|status OR buffer_* OR debug_scene|debug_bp_add|debug_bp_list|debug_launch|… (when host omits cdp_buffer/cdp_debug).", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "intent_*|stage_*|scene_*|status|buffer_*|debug_scene|debug_bp_add|debug_bp_remove|debug_bp_set|debug_bp_list|debug_bp_clear|debug_launch|debug_attach|debug_continue|debug_stop|debug_stop_context|…" },
            title = new { type = "string" },
            intent_id = new { type = "string" },
            stage_id = new { type = "string" },
            parent_id = new { type = "string" },
            scene_name = new { type = "string", description = "For stage_upsert bind; also alias of name for scene ops." },
            name = new { type = "string", description = "Scene name for park/switch." },
            status = new { type = "string", description = "pending|active|done|parked|deferred" },
            loot = new { type = "string" },
            focus_path = new { type = "string" },
            focus_line = new { type = "integer" },
            bind_stage_id = new { type = "string" },
            job_json = new { type = "string", description = "For stage_enqueue: {kind,file_path,solution_or_project_path,...}" },
            start_job = new { type = "boolean", description = "For stage_enqueue: start background IdeReport job (default true)." },
            path = new { type = "string", description = "buffer_* file path; debug_bp_add/remove source path" },
            file_path = new { type = "string", description = "Alias of path for debug bp_*" },
            line = new { type = "integer", description = "debug_bp_add/remove 1-based line" },
            condition = new { type = "string", description = "optional breakpoint condition" },
            workspace_path = new { type = "string", description = "debug_*: optional; session default after cdp_open" },
            target_path = new { type = "string", description = "debug_*: optional; session .csproj/.sln" },
            breakpoints = new { type = "array", description = "debug_bp_set only", items = new { type = "object" } },
            doc_id = new { type = "string", description = "buffer_*: open buffer id" },
            diagnose = new { type = "boolean" },
            flush = new { type = "boolean" },
            refresh = new { type = "boolean" },
            start_line = new { type = "integer" },
            end_line = new { type = "integer" },
            start_column = new { type = "integer" },
            end_column = new { type = "integer" },
            edit_op = new { type = "string", description = "buffer_edit: set_text|replace|replace_range" },
            text = new { type = "string" },
            old_string = new { type = "string" },
            new_string = new { type = "string" }
        },
        required = new[] { "op" }
    }),
    Meta("cdp_csx_check", "Compile CSX against allowlisted ScriptGlobals (Debug/Roslyn/Git/Verify/Mutate/Anui/Execution/Help). No tool dispatch. Returns DiagnosticItems with anchors.", new
    {
        type = "object",
        properties = new
        {
            code = new { type = "string", description = "CSX source (preferred)." },
            path = new { type = "string", description = "Optional path to .csx file if code omitted." }
        }
    }),
    Meta("cdp_csx_help", "Live CSX API help from XML docs (not a static man). op=toc|of. Prefer before inventing Symbol/SemanticMap APIs.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "toc (default) | of" },
            path = new { type = "string", description = "For op=of: Symbol, SemanticMap, Symbol.Named, Help, …" },
            max = new { type = "integer", description = "Cap facade/member rows (default 48 toc / 40 of)." }
        }
    }),
    Meta("cdp_evidence", "Project any pipe (build/test/publish/shell/auto) to evidence/v0 with Anchor wires — click locus, no line guessing.", new
    {
        type = "object",
        properties = new
        {
            kind = new { type = "string", description = "auto|build|test|publish|shell|csx|generic (default auto)" },
            text = new { type = "string", description = "Raw stdout/stderr/log body to project." },
            path = new { type = "string", description = "Optional file path if text omitted." }
        }
    }),
    Meta("cdp_csx_run", "Run CSX via ScriptHost. mode=run|dry_run. Dispatches to mounted domains (roslyn/git/debug/…).", new
    {
        type = "object",
        properties = new
        {
            code = new { type = "string", description = "CSX source (preferred)." },
            path = new { type = "string", description = "Optional path to .csx file if code omitted." },
            mode = new { type = "string", description = "run (default) | dry_run" },
            workspace_path = new { type = "string", description = "Plan.PrimaryRoot / WorkRoot for Fs + path remap (default cwd)." }
        }
    }),
    Meta("cdp_csx_run_plan", "Sandbox scoped to open project (GitRoot+PlanScope); overlay primary WIP under scope; promote merges plan delta (dirty elsewhere OK). Then cdp_csx_promote|discard.", new
    {
        type = "object",
        properties = new
        {
            code = new { type = "string" },
            path = new { type = "string" },
            workspace_path = new { type = "string", description = "Entry path (optional if cdp_open session). Resolved via git rev-parse --show-toplevel." },
            scope = new { type = "string", description = "Optional focus dir/file for PlanScope (default: session project root)." },
            promote_policy = new { type = "string", description = "overlap_safe (default) | strict_clean" }
        }
    }),
    Meta("cdp_csx_discard", "Remove worktree for plan_id; primary unchanged.", new
    {
        type = "object",
        properties = new { plan_id = new { type = "string" } },
        required = new[] { "plan_id" }
    }),
    Meta("cdp_csx_promote", "Apply plan delta to primary (default overlap_safe: dirty elsewhere OK; strict_clean = refuse any dirty). File sync of plan paths; conflict check first.", new
    {
        type = "object",
        properties = new
        {
            plan_id = new { type = "string" },
            promote_policy = new { type = "string", description = "overlap_safe | strict_clean (optional override)" }
        },
        required = new[] { "plan_id" }
    }),
    Meta("cdp_shell_scene", "[A] Agent terminal habitat map: all tabs (id, shell, cwd, state, last cmd/exit, preview). Prefer over switch→watch→switch.", new
    {
        type = "object",
        properties = new { }
    }),
    Meta("cdp_shell_run", "Run in named tab. Prefer argv[] (harness quotes). Or command string for pipes/| . Session cwd.", new
    {
        type = "object",
        properties = new
        {
            command = new { type = "string", description = "Raw shell line (pipes ok). Ignored if argv is non-empty." },
            argv = new
            {
                type = "array",
                items = new { type = "string" },
                description = "Structured args: [program, arg1, …]. Harness joins with shell-safe quoting."
            },
            tab = new { type = "string", description = "Tab id (default main). letters/digits/_- max 32." },
            cwd = new { type = "string", description = "Working directory (persists on tab). Alias: working_directory." },
            working_directory = new { type = "string", description = "Alias of cwd (Cursor Shell habit)." },
            shell = new { type = "string", description = "Prefer: pwsh | cmd | or unix shell path." },
            codepage = new { type = "integer", description = "Console/pipe code page; sticky on tab. Default 65001 (UTF-8)." },
            timeout_seconds = new { type = "integer", description = "1..600 (default 60). Ignored when background=true." },
            background = new { type = "boolean", description = "true = long-run in CDP process; poll scene/last; kill to stop." }
        }
    }),
    Meta("cdp_shell_history", "[A] Last N commands for a tab (cmd/cwd/exit/preview; no full stdout dump).", new
    {
        type = "object",
        properties = new
        {
            tab = new { type = "string" },
            n = new { type = "integer", description = "1..50 (default 20)." }
        }
    }),
    Meta("cdp_shell_rerun", "Re-run history entry (default last) on a tab — ↑ analogue.", new
    {
        type = "object",
        properties = new
        {
            tab = new { type = "string" },
            index = new { type = "integer", description = "History index; omit = last." },
            timeout_seconds = new { type = "integer" },
            background = new { type = "boolean" }
        }
    }),
    Meta("cdp_shell_last", "[C] Last result body for a tab (capped stdout/stderr). While running: live buffers.", new
    {
        type = "object",
        properties = new
        {
            tab = new { type = "string" },
            max_chars = new { type = "integer", description = "Cap per stream (default 12000)." }
        }
    }),
    Meta("cdp_shell_which", "[A] Active shell kind + exe + cwd (+ pid/state) for a tab.", new
    {
        type = "object",
        properties = new { tab = new { type = "string" } }
    }),
    Meta("cdp_shell_kill", "Kill running process on a tab (process tree).", new
    {
        type = "object",
        properties = new { tab = new { type = "string" } }
    }),
    Meta("cdp_shell_close", "Close tab (kills if running); removes it from the habitat scene.", new
    {
        type = "object",
        properties = new { tab = new { type = "string" } }
    })
];

Tool Meta(string name, string desc, object schema) => new()
{
    Name = name,
    Description = desc,
    InputSchema = JsonSerializer.SerializeToElement(schema)
};

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
                var text = await IdeCommandModule.ExecuteAsync(name, callArgs, cancellationToken);
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
        case "cdp_learn":
            return IdeLearnChannel.HandleJson(session, callArgs);
        case "cdp_files":
            return IdeFilesChannel.HandleJson(docStore, session, callArgs);
        case "cdp_ignite":
            return await IdeIgniteChannel.HandleJsonAsync(callArgs, cancellationToken);
        case "cdp_webcam":
            return IdeWebcamChannel.HandleJson(session, callArgs);
        case "cdp_pressure":
            return IdePressureChannel.HandleJson(session, callArgs);
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
