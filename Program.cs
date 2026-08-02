using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cdp.Core;
using Cdp.Evidence;
using Cdp.ScriptableIde;
using CdpMcp;
using CdpMcp.Backends;
using CdpMcp.IntentWorkspace;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Tool = ModelContextProtocol.Protocol.Tool;

IdeSeatProcessReclaim.Ensure();

var configPath = args.SkipWhile(a => a != "--config").Skip(1).FirstOrDefault()
    ?? Environment.GetEnvironmentVariable("CDP_MCP_CONFIG")
    ?? Path.Combine(AppContext.BaseDirectory, "config", "cdp-mcp.toml");
var settings = CdpSettings.Load(configPath);
IdeLanguageTools.Configure(settings.Languages, settings.LspPresets);
IdeCockpitHostChannel.Configure(settings.CockpitHost);
VendorCatalog.Configure(settings.Vendor);
IdeIgniteArmHost.EnsureStarted();

var session = new SessionContext();
IdeCockpitHostChannel.ProjectRootResolver = () => session.ProjectRoot;

var workspace = new WorkspaceDbHost(settings.IntentWorkspace.DatabasePath, session);
CdpProfile.OnStateRootChanged(() =>
{
    IdeSettingsStore.Invalidate();
    workspace.Invalidate();
});

IdeIgniteArmHost.BindFlightProbe(() =>
{
    if (workspace.State.ActiveStageId is null)
        return ContinuityFlight.NoActiveTask;
    try
    {
        workspace.Ensure();
        return IdeTaskManager.ProbeContinuityFlight(workspace.Store, workspace.State);
    }
    catch
    {
        return ContinuityFlight.Fly;
    }
});

/// <summary>Open Recent lives in WitDB — ensure store before push/list (cdp_open / CSX Open.*).</summary>
void EnsureOpenRecentWired() => workspace.Ensure();

IntentWorkspaceStore RequireWorkspace() => workspace.Require();

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
    () => ProgramHost.ShellDefaults(session));
IdeToolchainChannel.Configure(shellHabitat, () => ProgramHost.ShellDefaults(session));
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

List<Tool> BuildVisibleTools() =>
    VisibleToolCatalog.Build(new VisibleToolCatalogDeps
    {
        Session = session,
        AllAffordances = allAffordances,
        BuildMetaTools = BuildMetaTools,
        AnTools = anTools,
        TkTools = tkTools,
        FindTools = findTools,
        FailTools = failTools,
        DbgTools = dbgTools,
        BtTools = btTools,
        RoslynTools = roslynTools,
        GitTools = gitTools,
        HciTools = hciTools,
        AnuiTools = anuiTools
    });

List<Tool> BuildMetaTools() => MetaToolCatalog.Build();

var options = new McpServerOptions
{
    ServerInfo = new Implementation { Name = "CdpMcp", Version = mcpVersion },
    ProtocolVersion = "2024-11-05",
    ServerInstructions = ProgramHost.ServerInstructions,
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

var hostDeps = new ProgramHostDeps
{
    Session = session,
    DocStore = docStore,
    ByDomain = byDomain,
    Modules = modules,
    AllAffordances = allAffordances,
    Settings = settings,
    McpVersion = mcpVersion,
    SoftOrganMetaNames = VisibleToolCatalog.SoftOrganMetaNames,
    Pretty = Pretty,
    ShellHabitat = shellHabitat,
    McpOutlet = mcpOutlet,
    InternetBrowser = internetBrowser,
    IdeSettings = ideSettings,
    WorkspaceStore = workspace.Store,
    WorkspaceState = workspace.State,
    WorkspaceDbPath = workspace.DatabasePath,
    ServerRef = serverRef,
    NotifyListChanged = NotifyListChanged,
    EnsureOpenRecentWired = EnsureOpenRecentWired,
    EnsureWorkspaceDb = workspace.Ensure,
    BuildVisibleTools = BuildVisibleTools,
    BuildMetaTools = BuildMetaTools,
    RequireWorkspace = RequireWorkspace,
    RequireJobRunner = RequireJobRunner
};

Task<string> DispatchAsync(
    string name,
    IReadOnlyDictionary<string, JsonElement> callArgs,
    CancellationToken cancellationToken) =>
    ProgramHost.DispatchAsync(hostDeps, name, callArgs, cancellationToken);
void NotifyListChanged()
{
    if (serverRef is null) return;
    _ = serverRef.SendNotificationAsync(
        NotificationMethods.ToolListChangedNotification,
        cancellationToken: CancellationToken.None);
}

IdeCommandModule.Bind(DispatchAsync);

await using var stdio = new StdioServerTransport("CdpMcp");
await using var server = McpServer.Create(stdio, options);
serverRef = server;
CdpClientWorkspace.Wire(server);
Console.Error.WriteLine($"CdpMcp {mcpVersion} backends=[{string.Join(",", byDomain.Keys)}] context={CdpEnumParse.ToWire(session.Phase)}/{CdpEnumParse.ToWire(session.Object)} isolation={CdpProfile.Kind}");
await server.RunAsync();