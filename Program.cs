using System.Collections.Frozen;
using System.Text.Json;
using Cdp.Core;
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
    ?? Environment.GetEnvironmentVariable("CDP_MCP_CONFIG");
var settings = CdpSettings.Load(configPath);
IdeLanguageTools.Configure(settings.Languages, settings.LspPresets);
VendorCatalog.Configure(settings.Vendor);

var workspaceDbPath = settings.IntentWorkspace.DatabasePath
    ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "cdp-mcp", "intent-workspace.witdb");
IntentWorkspaceStore? workspaceStore = null;
var workspaceState = new IntentWorkspaceState { DatabasePath = workspaceDbPath };
void EnsureWorkspaceDb()
{
    if (workspaceStore is not null) return;
    Directory.CreateDirectory(Path.GetDirectoryName(workspaceDbPath)!);
    var wsOptions = new DbContextOptionsBuilder<IntentWorkspaceDbContext>()
        .UseWitDb($"Data Source={workspaceDbPath}")
        .Options;
    using (var boot = new IntentWorkspaceDbContext(wsOptions))
        boot.Database.EnsureCreated();
    workspaceStore = new IntentWorkspaceStore(wsOptions);
    workspaceStore.EnsureOpenRecentTable();
    workspaceStore.MigrateLegacyOpenRecentJsonIfPresent();
    OpenRecentStore.Configure(new WitDbOpenRecentBackend(workspaceStore, workspaceDbPath));
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

var session = new SessionContext();
var docStore = new DocumentBufferStore();
IdeLanguageTools.BindDocumentStore(docStore);
var shellHabitat = new TerminalMcp.Core.ShellHabitat();
if (CdpEnumParse.TryParsePhase(settings.DefaultPhase, out var dp)) session.Phase = dp;
if (CdpEnumParse.TryParseObject(settings.DefaultObject, out var dobj)) session.Object = dobj;

var mcpVersion = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.4.0";
var Pretty = new JsonSerializerOptions { WriteIndented = true };
McpServer? serverRef = null;

List<Tool> BuildVisibleTools()
{
    var meta = BuildMetaTools();
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
    Meta("cdp_man", "CDP ops manual. tool= omit for TOC; or cdp_health|cdp_capabilities|cdp_context|cdp_tools|cdp_session|cdp_shell_*.", new
    {
        type = "object",
        properties = new { tool = new { type = "string" } }
    }),
    Meta("cdp_health", "Backend health + runtime (version/exe/build_utc/pending_update). Optional explain_tool=prefixed name → why missing from shortlist.", new
    {
        type = "object",
        properties = new
        {
            explain_tool = new { type = "string", description = "Prefixed tool name to explain visibility." }
        }
    }),
    Meta("cdp_capabilities", "Mounted domains + layers.memory facets/roots + affordance counts.", new { type = "object", properties = new { } }),
    Meta("cdp_context", "Get/set session phase+object(+intent[+language]). Triggers tools/list_changed.", new
    {
        type = "object",
        properties = new
        {
            phase = new { type = "string", description = "recall|explore|clarify|plan|act|verify|handoff" },
            @object = new { type = "string", description = "kb|code|repo|task|finding|process|issue|session" },
            intent = new { type = "string", description = "optional find|cite|change|verify|record|ship" },
            language = new { type = "string", description = "optional language id/alias from [languages] config; empty clears" },
            get = new { type = "boolean", description = "If true, only return current context." }
        }
    }),
    Meta("cdp_open", "Open a project path: detect .sln/.csproj/tsconfig → session root+language+scm_root; list_changed. After open, git_* may omit workspace_path (defaults to scm_root). Prefer before go_to_definition. Omit path to reopen Recent[0]; or recent_index=N.", new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "File or directory (.sln, .csproj, tsconfig.json, source file, or folder). Optional if recent_index set or Recent non-empty." },
            recent_index = new { type = "integer", description = "Optional 0-based Open Recent index (0 = last opened)." }
        }
    }),
    Meta("cdp_buffer", "File buffer plane: op=scene|open|create|read|edit|diagnostics|close. Instant Save: edit/close flush=true by default (disk on every edit). flush=false = batch in memory; close+dirty needs discard=true. csharp: prefer edit_op=anchor [F:;M:;K:] (surgical); replace/replace_range are fallbacks (replace tolerates LF↔CRLF). Diagnostics: scope=syntax|project|solution; cached while version unchanged (force=true). edit diagnose default true (csharp syntax). Parallel OK.", new
    {
        type = "object",
        properties = new
        {
            op = new { type = "string", description = "scene|open|create|read|edit|diagnostics|close" },
            path = new { type = "string" },
            doc_id = new { type = "string" },
            diagnose = new { type = "boolean", description = "open default false; create/edit default true (csharp: syntax)" },
            flush = new { type = "boolean", description = "edit/close default true (Instant Save). false = keep dirty in memory (batch)." },
            discard = new { type = "boolean", description = "close only: with flush=false, required to drop dirty buffer without writing." },
            refresh = new { type = "boolean", description = "open: reload from disk; diagnostics: soft prefer-cache when false" },
            force = new { type = "boolean", description = "diagnostics: recompute even if version unchanged" },
            scope = new { type = "string", description = "diagnostics: syntax|project|solution (default syntax)" },
            overwrite = new { type = "boolean", description = "create: allow replace existing file" },
            allow_shrink = new { type = "boolean", description = "edit set_text: required when new body is shorter than on-disk file" },
            start_line = new { type = "integer" },
            end_line = new { type = "integer" },
            edit_op = new { type = "string", description = "edit: anchor|set_text|replace|replace_range — prefer anchor" },
            anchor = new { type = "string", description = "edit_op=anchor: csharp [F:;M:;K:] or xml [F:;X:path;A:attr?][+K:Element]" },
            at = new { type = "string", description = "Alias of anchor" },
            text = new { type = "string", description = "edit set_text / create body / anchor replacement" },
            old_string = new { type = "string" },
            new_string = new { type = "string", description = "replace; also alias of text for anchor" },
            start_column = new { type = "integer" },
            end_column = new { type = "integer" }
        },
        required = new[] { "op" }
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
    Meta("cdp_build", "IDE Build: session project after cdp_open. Harness picks projection (csharp→dotnet / typescript→npm|tsc). Prefer over shell.", new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "Optional .sln/.csproj/tsconfig; default = session after cdp_open" },
            solution_path = new { type = "string", description = "Alias of path (csharp)" },
            configuration = new { type = "string", description = "Debug|Release (csharp)" },
            framework = new { type = "string" },
            no_restore = new { type = "boolean" },
            include_raw_output = new { type = "boolean" },
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
    Meta("cdp_test", "IDE Test: session project. csharp→dotnet test; typescript→npm test. Prefer over shell.", new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "Optional .sln/.csproj/package root; default = session" },
            solution_path = new { type = "string", description = "Alias of path (csharp)" },
            configuration = new { type = "string" },
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
    Meta("cdp_project_create", "Scaffold project. csharp: tfm_policy (+ tfm=). typescript: engine_policy (+ engines=). Policies prefer_most_used|latest|lts|specified — LTS from vendor meta (dotnet releases-index release-type / node dist lts). Optional open=true.", new
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
    Meta("cdp_tools", "Shortlist catalog=f(phase,object[,intent][,language]) — agent command palette preview.", new
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
    Meta("cdp_session", "Agent-IDE session plane: context + shortlist + health + optional debug stop_context + pack dogfood (definitions/process/procedure) + continuity hint.", new
    {
        type = "object",
        properties = new
        {
            explain_tool = new { type = "string", description = "Optional: why this tool is hidden/visible." },
            include_debug = new { type = "boolean", description = "Include debug_stop_context when debug mounted (default true)." },
            include_pack = new { type = "boolean", description = "Embed LLM-native pack process+procedure+debug-radius (default true)." },
            pack_id = new { type = "string", description = "Pack id (default epistemic-scene)." },
            process_id = new { type = "string", description = "Process id (default bug-radius-shrink; try applicability-then-infer / curiosity-kolb-loop)." },
            procedure_id = new { type = "string", description = "Optional when-card id (default kolb-journal-park when process is curiosity-kolb-loop)." },
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
            status = new { type = "string", description = "pending|active|done|parked" },
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
    Meta("cdp_csx_check", "Compile CSX against allowlisted ScriptGlobals (Debug/Roslyn/Git/Verify/Mutate/Anui/Execution). No tool dispatch.", new
    {
        type = "object",
        properties = new
        {
            code = new { type = "string", description = "CSX source (preferred)." },
            path = new { type = "string", description = "Optional path to .csx file if code omitted." }
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
    Meta("cdp_shell_scene", "Agent terminal habitat map: all tabs (id, shell, cwd, state, last cmd/exit, preview). Prefer over switch→watch→switch.", new
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
            cwd = new { type = "string", description = "Working directory (persists on tab)." },
            shell = new { type = "string", description = "Prefer: pwsh | cmd | or unix shell path." },
            codepage = new { type = "integer", description = "Console/pipe code page; sticky on tab. Default 65001 (UTF-8)." },
            timeout_seconds = new { type = "integer", description = "1..600 (default 60). Ignored when background=true." },
            background = new { type = "boolean", description = "true = long-run in CDP process; poll scene/last; kill to stop." }
        }
    }),
    Meta("cdp_shell_history", "Last N commands for a tab (cmd/cwd/exit/preview; no full stdout dump).", new
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
    Meta("cdp_shell_last", "Last result body for a tab (capped stdout/stderr). While running: live buffers.", new
    {
        type = "object",
        properties = new
        {
            tab = new { type = "string" },
            max_chars = new { type = "integer", description = "Cap per stream (default 12000)." }
        }
    }),
    Meta("cdp_shell_which", "Active shell kind + exe + cwd (+ pid/state) for a tab.", new
    {
        type = "object",
        properties = new { tab = new { type = "string" } }
    }),
    Meta("cdp_shell_kill", "Kill running process on a tab (process tree).", new
    {
        type = "object",
        properties = new { tab = new { type = "string" } }
    }),
    Meta("cdp_shell_close", "Close tab (kills if running); frees a slot toward max_tabs.", new
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
        "Always: cdp_session (omnibus) / cdp_context / cdp_open / cdp_buffer(op) / cdp_debug(op) / cdp_recent / cdp_build|cdp_run|cdp_test / cdp_pkg_* / cdp_work (intent scenes) / cdp_tools (palette) / cdp_health (explain_tool?). " +
        "Mutate SSOT: cdp_buffer (open|create|edit); Instant Save flush=true on edit/close (flush=false batches; close discard=true to drop). Prefer edit_op=anchor [F:;M:;K:] for csharp. Cursor host Write bypasses PathMutateGate. " +
        "Buffer plane: cdp_buffer op=open|edit|… — edit returns diagnostics in-result (almost-online while you keep the turn). " +
        "Debug plane: cdp_debug op=bp_add|launch|stop_context|… — session defaults after cdp_open; .csproj is BP key, launch resolves dll under bin/; JSON file is storage only. " +
        "IDE verbs (harness routes LSP): go_to_definition, find_usages, get_document_symbols, get_symbol_at_position, get_diagnostics, resolve_project_root, get_workspace_navigation_context. " +
        "Prefer cdp_build/cdp_run/cdp_test/cdp_pkg_*/cdp_project_*/cdp_sln_* over shell for session project. " +
        "Agent shell habitat: cdp_shell_* = primary IDE terminal; sibling terminal-mcp (terminal_*) = escape only. " +
        "CSX: cdp_csx_check | cdp_csx_run (Symbol/SemanticMap/Work Enqueue*) | cdp_csx_run_plan | promote | discard. " +
        "Domain tools prefixed " + DomainPrefixHint + " (roslyn_* = legacy aliases; prefer bare IDE verbs). " +
        "ListTools = meta + bare IDE verbs + ≤10 domain shortlist (deduped underlying; not full union). " +
        "Too many tools = agent thrash — use cdp_context to retarget, cdp_tools to preview, cdp_session for pack embed. " +
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
                var text = await DispatchAsync(name, callArgs, cancellationToken);
                return new CallToolResult
                {
                    Content = [new TextContentBlock { Text = ResponseCaps.CapText(text) }]
                };
            }
            catch (OperationCanceledException)
            {
                return new CallToolResult
                {
                    Content = [new TextContentBlock { Text = $"# Aborted: {(string.IsNullOrEmpty(name) ? "(unknown)" : name)}" }]
                };
            }
            catch (Exception ex)
            {
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
    if (DocumentEditPlane.IsDocTool(name))
        return await DocumentEditPlane.DispatchAsync(name, docStore, session, byDomain, callArgs, cancellationToken)
            .ConfigureAwait(false);

    if (DebugPlane.IsDebugPlaneTool(name))
        return await DebugPlane.DispatchAsync(session, byDomain, callArgs, cancellationToken)
            .ConfigureAwait(false);

    if (name.StartsWith("cdp_", StringComparison.Ordinal))
        return await DispatchMetaAsync(name, callArgs, cancellationToken).ConfigureAwait(false);

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
    CancellationToken cancellationToken)
{
    switch (name)
    {
        case "cdp_man":
            if (callArgs.TryGetValue("tool", out var t) && t.GetString() is { Length: > 0 } tool)
                return $"Manual: {tool} — see tool description; domain ops via prefixed tools / sibling man.";
            return "TOC: cdp_session (omnibus plane + pack dogfood), cdp_health(explain_tool?), cdp_capabilities, " +
                   "cdp_context(phase,object,intent?,language?), cdp_open(path), cdp_build|cdp_run|cdp_test (session IDE lifecycle), " +
                   "cdp_buffer(op=scene|open|read|edit|diagnostics|close) file buffer SSOT; edit returns diagnostics, " +
                   "cdp_debug(op=scene|bp_add|bp_remove|bp_set|bp_list|bp_clear|launch|…) debug plane; session defaults, not breakpoints JSON, " +
                   "cdp_pkg_find|list|add|remove|update|outdated, cdp_project_create|list|close|add_to_sln, " +
                   "cdp_sln_create|list|projects|add|remove, " +
                   "cdp_work(op=intent|stage|scene), cdp_tools(... palette), " +
                   "IDE: go_to_definition|find_usages|get_document_symbols|get_symbol_at_position|get_diagnostics|resolve_project_root|get_workspace_navigation_context, " +
                   "cdp_csx_check / cdp_csx_run / cdp_csx_run_plan / promote / discard. " +
                   "cdp_shell_scene|run|history|rerun|last|which|kill|close (agent terminal; background long-run). " +
                   "Pack: get_definition|list_pack|get_process|get_procedure|radius_gate_check. " +
                   "Domain prefixes: memory_world_ memory_project_ memory_task_ memory_session_ memory_skill_ " +
                   "memory_self_finding_ memory_self_failure_ debug_ build_ roslyn_ git_ codebase_index_ anui_. " +
                   "Agent-IDE pillars: session plane, shared truth, affordance nav, continuity, evidence-first, self-ops. " +
                   "Order: Agent Env first; CIDE projector later.";
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
                    "Host (Cursor) owns MCP process restart; agent cannot self-reload. " +
                    "Soft deploy stages to <target>.next + cdp-pending-update.json; apply with publish -Mode hard then Reload MCP. " +
                    "Prefer cdp_health + explain_tool before guessing missing tools."
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
                meta_tool_names = BuildMetaTools().Select(t => t.Name).ToArray(),
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
            if (callArgs.TryGetValue("phase", out var ph) && CdpEnumParse.TryParsePhase(ph.GetString(), out var newPhase))
            {
                session.Phase = newPhase;
                changed = true;
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
            return session.ToJson() + (changed ? "\n# list_changed: shortlist refreshed for new context" : "");
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
            var payload = IdeLanguageTools.ApplyOpen(session, open);
            shellHabitat.SyncSessionCwd(session.ProjectRoot);
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
                    IdeLanguageTools.ApplyOpen(session, open);
                    NotifyListChanged();
                }
            }

            return step.ToJson();
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
                    IdeLanguageTools.ApplyOpen(session, open);
                    NotifyListChanged();
                }
            }

            return step.ToJson();
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
                    var payload = IdeLanguageTools.ApplyOpen(session, open);
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
            string? cwd = callArgs.TryGetValue("cwd", out var cwdEl) ? cwdEl.GetString() : null;
            string? shell = callArgs.TryGetValue("shell", out var shEl) ? shEl.GetString() : null;
            int? timeout = callArgs.TryGetValue("timeout_seconds", out var toEl) && toEl.TryGetInt32(out var to)
                ? to
                : null;
            var background = callArgs.TryGetValue("background", out var bgEl) && bgEl.ValueKind == JsonValueKind.True;
            int? codepage = callArgs.TryGetValue("codepage", out var cpEl) && cpEl.TryGetInt32(out var cp)
                ? cp
                : null;
            return shellHabitat.Run(ShellDefaults(session), cmd, tab, cwd, shell, timeout, background, codepage, argv);
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
                : null;
            var background = callArgs.TryGetValue("background", out var bgEl) && bgEl.ValueKind == JsonValueKind.True;
            return shellHabitat.Rerun(ShellDefaults(session), tab, index, timeout, background);
        }
        case "cdp_shell_last":
        {
            string? tab = callArgs.TryGetValue("tab", out var tabEl) ? tabEl.GetString() : null;
            var maxChars = callArgs.TryGetValue("max_chars", out var mcEl) && mcEl.TryGetInt32(out var mc)
                ? mc
                : 0;
            return shellHabitat.Last(tab, maxChars);
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

async Task<string> ResolveCsxSourceAsync(IReadOnlyDictionary<string, JsonElement> callArgs)
{
    if (callArgs.TryGetValue("code", out var c) && c.GetString() is { Length: > 0 } code)
        return code;
    if (!callArgs.TryGetValue("path", out var p) || p.GetString() is not { Length: > 0 } path)
        throw new ArgumentException("code or path is required for CSX tools.");

    var candidates = new List<string>();
    void AddCandidate(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return;
        try
        {
            var full = Path.GetFullPath(candidate);
            if (!candidates.Contains(full, StringComparer.OrdinalIgnoreCase))
                candidates.Add(full);
        }
        catch
        {
            // ignore invalid path candidates
        }
    }

    AddCandidate(path);
    // Dual folder spellings on this machine (space vs compacted).
    if (path.Contains("Personal Cursor Folder", StringComparison.OrdinalIgnoreCase))
        AddCandidate(path.Replace("Personal Cursor Folder", "PersonalCursorFolder", StringComparison.OrdinalIgnoreCase));
    if (path.Contains("PersonalCursorFolder", StringComparison.OrdinalIgnoreCase)
        && !path.Contains("Personal Cursor Folder", StringComparison.OrdinalIgnoreCase))
        AddCandidate(path.Replace("PersonalCursorFolder", "Personal Cursor Folder", StringComparison.OrdinalIgnoreCase));

    if (callArgs.TryGetValue("workspace_path", out var wp) && wp.GetString() is { Length: > 0 } root)
    {
        if (!Path.IsPathRooted(path))
            AddCandidate(Path.Combine(root, path));
        AddCandidate(Path.Combine(root, "_dogfood-w23-live", Path.GetFileName(path)));
    }

    AddCandidate(Path.Combine(Environment.CurrentDirectory, path));
    AddCandidate(Path.Combine(Environment.CurrentDirectory, "_dogfood-w23-live", Path.GetFileName(path)));

    // Session project → owning git root → sibling dogfood stand.
    if (session.ProjectRoot is { Length: > 0 } projectRoot)
    {
        AddCandidate(Path.Combine(projectRoot, path));
        AddCandidate(Path.Combine(projectRoot, Path.GetFileName(path)));
        try
        {
            var gitRoot = GitRootResolver.ResolveGitRoot(projectRoot);
            AddCandidate(Path.Combine(gitRoot, path));
            AddCandidate(Path.Combine(gitRoot, "_dogfood-w23-live", Path.GetFileName(path)));
            if (!Path.IsPathRooted(path))
                AddCandidate(Path.Combine(gitRoot, path));
        }
        catch
        {
            // not a git path — skip
        }
    }

    if (session.SolutionOrProjectPath is { Length: > 0 } sol)
    {
        var solDir = Path.GetDirectoryName(sol);
        if (!string.IsNullOrEmpty(solDir))
        {
            AddCandidate(Path.Combine(solDir, Path.GetFileName(path)));
            try
            {
                var gitRoot = GitRootResolver.ResolveGitRoot(solDir);
                AddCandidate(Path.Combine(gitRoot, "_dogfood-w23-live", Path.GetFileName(path)));
            }
            catch { /* ignore */ }
        }
    }

    foreach (var candidate in candidates)
    {
        if (File.Exists(candidate))
            return await File.ReadAllTextAsync(candidate).ConfigureAwait(false);
    }

    throw new ArgumentException(
        $"CSX path not found: {path}. Tried: {string.Join(" | ", candidates)}");
}

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
        _ => throw new ArgumentException(
            $"Unknown cdp_work op '{op}'. Use intent_*|stage_*|scene_*|status (incl. stage_enqueue|stage_get).")
    };
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

await using var stdio = new StdioServerTransport("CdpMcp");
await using var server = McpServer.Create(stdio, options);
serverRef = server;
Console.Error.WriteLine($"CdpMcp {mcpVersion} backends=[{string.Join(",", byDomain.Keys)}] context={CdpEnumParse.ToWire(session.Phase)}/{CdpEnumParse.ToWire(session.Object)}");
await server.RunAsync();
