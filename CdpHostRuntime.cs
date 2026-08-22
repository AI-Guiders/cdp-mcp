using System.Collections.Frozen;
using System.Text.Json;
using Cdp.Core;
using Cdp.Evidence;
using Cdp.ScriptableIde;
using CdpMcp.Backends;
using CdpMcp.IntentWorkspace;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TerminalMcp.Core;
using Tool = ModelContextProtocol.Protocol.Tool;

namespace CdpMcp;

/// <summary>Durable CDP substrate — shared by CdpService (HTTP) and legacy stdio monolith.</summary>
internal sealed class CdpHostRuntime : IAsyncDisposable
{
    readonly SessionContext _session;
    readonly WorkspaceDbHost _workspace;
    readonly DocumentBufferStore _docStore;
    readonly ShellHabitat _shellHabitat;
    readonly McpOutletHabitat _mcpOutlet;
    readonly InternetBrowserHabitat _internetBrowser;
    readonly IdeSettingsHabitat _ideSettings;
    readonly List<ICdpBackendModule> _modules;
    readonly IReadOnlyDictionary<string, ICdpBackendModule> _byDomain;
    readonly ToolAffordance[] _allAffordances;
    readonly CdpSettings _settings;
    readonly string _mcpVersion;
    readonly JsonSerializerOptions _pretty;
    readonly Dictionary<string, Tool> _anTools;
    readonly Dictionary<string, Tool> _tkTools;
    readonly Dictionary<string, Tool> _findTools;
    readonly Dictionary<string, Tool> _failTools;
    readonly Dictionary<string, Tool> _dbgTools;
    readonly Dictionary<string, Tool> _btTools;
    readonly Dictionary<string, Tool> _roslynTools;
    readonly Dictionary<string, Tool> _gitTools;
    readonly Dictionary<string, Tool> _hciTools;
    readonly Dictionary<string, Tool> _anuiTools;
    readonly ProgramHostDeps _hostDeps;
    readonly IdeReportJobRunner? _jobRunner;
    readonly IDisposable? _diskSyncWatch;
    readonly IDisposable? _intercomCannon;

    McpServer? _serverRef;
    readonly CdpCapabilitiesRevision _capabilitiesRevision = new();

    readonly CdpTenantRegistry _tenantRegistry;

    CdpHostRuntime(
        SessionContext session,
        WorkspaceDbHost workspace,
        DocumentBufferStore docStore,
        ShellHabitat shellHabitat,
        McpOutletHabitat mcpOutlet,
        InternetBrowserHabitat internetBrowser,
        IdeSettingsHabitat ideSettings,
        List<ICdpBackendModule> modules,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        ToolAffordance[] allAffordances,
        CdpSettings settings,
        string mcpVersion,
        JsonSerializerOptions pretty,
        Dictionary<string, Tool> anTools,
        Dictionary<string, Tool> tkTools,
        Dictionary<string, Tool> findTools,
        Dictionary<string, Tool> failTools,
        Dictionary<string, Tool> dbgTools,
        Dictionary<string, Tool> btTools,
        Dictionary<string, Tool> roslynTools,
        Dictionary<string, Tool> gitTools,
        Dictionary<string, Tool> hciTools,
        Dictionary<string, Tool> anuiTools,
        ProgramHostDeps hostDeps,
        IdeReportJobRunner? jobRunner,
        IDisposable? diskSyncWatch,
        IDisposable? intercomCannon,
        CdpTenantRegistry tenantRegistry)
    {
        _tenantRegistry = tenantRegistry;
        _session = session;
        _workspace = workspace;
        _docStore = docStore;
        _shellHabitat = shellHabitat;
        _mcpOutlet = mcpOutlet;
        _internetBrowser = internetBrowser;
        _ideSettings = ideSettings;
        _modules = modules;
        _byDomain = byDomain;
        _allAffordances = allAffordances;
        _settings = settings;
        _mcpVersion = mcpVersion;
        _pretty = pretty;
        _anTools = anTools;
        _tkTools = tkTools;
        _findTools = findTools;
        _failTools = failTools;
        _dbgTools = dbgTools;
        _btTools = btTools;
        _roslynTools = roslynTools;
        _gitTools = gitTools;
        _hciTools = hciTools;
        _anuiTools = anuiTools;
        _hostDeps = hostDeps;
        _jobRunner = jobRunner;
        _diskSyncWatch = diskSyncWatch;
        _intercomCannon = intercomCannon;
    }

    internal CdpSettings Settings => _settings;
    internal SessionContext Session => _session;
    internal ProgramHostDeps HostDeps => _hostDeps;
    internal string McpVersion => _mcpVersion;
    internal IReadOnlyDictionary<string, ICdpBackendModule> Backends => _byDomain;
    internal long CapabilitiesRevision => _capabilitiesRevision.Current;

    internal IAsyncEnumerable<long> WatchCapabilitiesRevisionAsync(CancellationToken cancellationToken = default) =>
        _capabilitiesRevision.WatchAsync(cancellationToken);

    internal static async Task<CdpHostRuntime> CreateAsync(string configPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = CdpSettings.Load(configPath);
        var lspPresets = settings.LspPresets.ToList();
        if (!lspPresets.Any(p => p.Id.Equals("powershell", StringComparison.OrdinalIgnoreCase)))
            lspPresets.Add(Ps1EditorServices.BuildLspPreset());
        IdeLanguageTools.Configure(settings.Languages, lspPresets);
        IdeCockpitHostChannel.Configure(settings.CockpitHost, configPath);
        VendorCatalog.Configure(settings.Vendor);
        IdeIgniteArmHost.EnsureStarted();

        var session = new SessionContext();

        var workspace = new WorkspaceDbHost(settings.IntentWorkspace.DatabasePath, session);
        CdpProfile.OnStateRootChanged(() =>
        {
            IdeSettingsStore.Invalidate();
            workspace.Invalidate();
        });

        IdeIgniteArmHost.BindFlightProbe(() =>
        {
            var ws = CdpTenantExecutionContext.CurrentSlice?.Workspace ?? workspace;
            if (ws.State.ActiveStageId is null)
                return ContinuityFlight.NoActiveTask;
            try
            {
                ws.Ensure();
                return IdeTaskManager.ProbeContinuityFlight(ws.Store!, ws.State);
            }
            catch
            {
                return ContinuityFlight.Fly;
            }
        });
        IdeIgniteArmHost.BindCitizenFocusLane(() =>
        {
            var ws = CdpTenantExecutionContext.CurrentSlice?.Workspace ?? workspace;
            ws.Ensure();
            var store = ws.Store;
            if (store is null)
                return;
            var (who, _) = CitizenGlassDialogBridge.ResolveCitizenFace();
            store.WorkFocusSwitchLane(ws.State, who);
        });

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
        CitizenRouteHost.BuildModuleResolver = () => byDomain.GetValueOrDefault("build");
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
        var diskSyncWatch = DocumentDiskSyncWatcher.Start(docStore);
        var intercomCannon = IntercomVoiceCannonWatcher.Start();
        IdeIgniteArmHost.StartHildWatch();
        IdeIgniteOomWatch.Start();
        CitizenGlassDialogBridge.Start();
        GlassIgniteCmdBridge.Start();
        GlassEicasCmdBridge.Start();
        IdeIgniteArmHost.PublishGlass();
        IdeLanguageTools.BindDocumentStore(docStore);
        var shellHabitat = new ShellHabitat();
        IdeCockpitHostChannel.ProjectRootResolver = () =>
            CdpTenantExecutionContext.CurrentSlice?.Session.ProjectRoot ?? session.ProjectRoot;
        CitizenRouteHost.SessionResolver = () =>
            CdpTenantExecutionContext.CurrentSlice?.Session ?? session;
        CitizenRouteHost.ShellHabitatResolver = () =>
            CdpTenantExecutionContext.CurrentSlice?.Shell ?? shellHabitat;
        CitizenRouteHost.ShellDefaultsResolver = () =>
            ProgramHost.ShellDefaults(CdpTenantExecutionContext.CurrentSlice?.Session ?? session);
        CitizenRouteHost.ByDomainResolver = () => byDomain;
        shellHabitat.Finished += IdeShellIgnite.OnShellFinished;
        var mcpOutlet = new McpOutletHabitat();
        var internetBrowser = new InternetBrowserHabitat();
        CitizenRouteHost.BrowserHabitatResolver = () => internetBrowser;
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
        if (IdeSettingsStore.TryGet("session.default_phase", out var up)
            && CdpEnumParse.TryParsePhase(up, out var udp))
            session.Phase = udp;
        if (IdeSettingsStore.TryGet("session.default_object", out var uo)
            && CdpEnumParse.TryParseObject(uo, out var udo))
            session.Object = udo;

        var mcpVersion = typeof(CdpHostRuntime).Assembly.GetName().Version?.ToString(3) ?? "0.4.0";
        var pretty = new JsonSerializerOptions { WriteIndented = true };

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

        var hostDeps = new ProgramHostDeps
        {
            Session = session,
            DocStore = docStore,
            ByDomain = byDomain,
            Modules = modules,
            AllAffordances = allAffordances,
            Settings = settings,
            McpVersion = mcpVersion,
            SoftInstrumentMetaNames = VisibleToolCatalog.SoftInstrumentMetaNames,
            Pretty = pretty,
            ShellHabitat = shellHabitat,
            McpOutlet = mcpOutlet,
            InternetBrowser = internetBrowser,
            IdeSettings = ideSettings,
            WorkspaceStore = workspace.Store,
            WorkspaceState = workspace.State,
            WorkspaceDbPath = workspace.DatabasePath,
            ServerRef = null,
            EnsureOpenRecentWired = EnsureOpenRecentWired,
            EnsureWorkspaceDb = workspace.Ensure,
            BuildVisibleTools = BuildVisibleTools,
            BuildMetaTools = BuildMetaTools,
            RequireWorkspace = RequireWorkspace,
            RequireJobRunner = RequireJobRunner
        };

        IdeStageCycle.SetEnsure(workspace.Ensure);
        workspace.Ensure();
        IdeStageCycle.Bind(
            workspace.Require(),
            () => workspace.State,
            () => CdpEnumParse.ToWire(session.Phase));

        Task<string> DispatchAsync(
            string name,
            IReadOnlyDictionary<string, JsonElement> callArgs,
            CancellationToken ct) =>
            ProgramHost.DispatchAsync(hostDeps, name, callArgs, ct);
        CitizenRouteHost.MetaDispatchResolver = DispatchAsync;
        IdeCommandModule.Bind(DispatchAsync);

        var defaultSlice = CdpTenantSliceFactory.WrapLegacy(
            CdpTenantKey.LegacyDefault,
            session,
            docStore,
            workspace,
            shellHabitat,
            ideSettings,
            diskSyncWatch,
            CdpProfile.StateRoot);

        var kernel = new CdpSharedKernel
        {
            ConfigPath = configPath,
            Settings = settings,
            Modules = modules,
            ByDomain = byDomain,
            AllAffordances = allAffordances,
            McpVersion = mcpVersion,
            Pretty = pretty,
            McpOutlet = mcpOutlet,
            InternetBrowser = internetBrowser,
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
        };
        var tenantRegistry = new CdpTenantRegistry(kernel, defaultSlice);
        IdeIgniteArmHost.BindTenantResolver(key => tenantRegistry.Resolve(key));

        var runtime = new CdpHostRuntime(
            session,
            workspace,
            docStore,
            shellHabitat,
            mcpOutlet,
            internetBrowser,
            ideSettings,
            modules,
            byDomain,
            allAffordances,
            settings,
            mcpVersion,
            pretty,
            anTools,
            tkTools,
            findTools,
            failTools,
            dbgTools,
            btTools,
            roslynTools,
            gitTools,
            hciTools,
            anuiTools,
            hostDeps,
            jobRunner,
            diskSyncWatch,
            intercomCannon,
            tenantRegistry);

        hostDeps.NotifyListChanged = () => runtime.NotifyListChanged();
        return runtime;
    }

    internal List<Tool> ListTools() => _hostDeps.BuildVisibleTools();

    internal int TenantCount => _tenantRegistry.ActiveCount;

    internal IReadOnlyList<CdpTenantSnapshot> TenantSnapshots => _tenantRegistry.SnapshotActive();

    internal async Task<CdpInvokeResult> InvokeToolAsync(
        string name,
        IReadOnlyDictionary<string, JsonElement> callArgs,
        CancellationToken cancellationToken,
        CdpTenantKey? tenantKey = null)
    {
        callArgs ??= FrozenDictionary<string, JsonElement>.Empty;
        var slice = _tenantRegistry.Resolve(tenantKey);
        using var profileScope = slice.EnterScope();
        using var execScope = CdpTenantExecutionContext.Enter(slice);
        return await InvokeToolCoreAsync(name, callArgs, cancellationToken, slice).ConfigureAwait(false);
    }

    async Task<CdpInvokeResult> InvokeToolCoreAsync(
        string name,
        IReadOnlyDictionary<string, JsonElement> callArgs,
        CancellationToken cancellationToken,
        CdpTenantSlice slice)
    {
        callArgs ??= FrozenDictionary<string, JsonElement>.Empty;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_serverRef is not null)
                await CdpClientWorkspace.RefreshAsync(_serverRef, cancellationToken).ConfigureAwait(false);
            CdpClientWorkspace.EnsureSessionFallback(slice.Session);
            var text = await IdeToolCallWatch.RunAsync(
                    name,
                    callArgs,
                    ct => IdeCommandModule.ExecuteAsync(name, callArgs, ct),
                    cancellationToken)
                .ConfigureAwait(false);
            return new CdpInvokeResult(text, IsError: false);
        }
        catch (OperationCanceledException)
        {
            ToolMediaOutbox.Clear();
            return new CdpInvokeResult(
                $"# Aborted: {(string.IsNullOrEmpty(name) ? "(unknown)" : name)}",
                IsError: false);
        }
        catch (Exception ex)
        {
            ToolMediaOutbox.Clear();
            return new CdpInvokeResult($"Error: {ex.Message}", IsError: true);
        }
    }

    internal CallToolResult ToCallToolResult(CdpInvokeResult result) =>
        new()
        {
            Content = ToolMediaOutbox.BuildContent(result.Body),
            IsError = result.IsError
        };

    internal McpServerOptions BuildMcpServerOptions() =>
        new()
        {
            ServerInfo = new Implementation { Name = "CdpService", Version = _mcpVersion },
            ProtocolVersion = "2024-11-05",
            ServerInstructions = ProgramHost.ServerInstructions,
            Capabilities = new ServerCapabilities
            {
                Tools = new ToolsCapability { ListChanged = true }
            },
            Handlers = new McpServerHandlers
            {
                ListToolsHandler = (_, _) => ValueTask.FromResult(new ListToolsResult { Tools = ListTools() }),
                CallToolHandler = async (request, cancellationToken) =>
                {
                    var name = request.Params?.Name ?? "";
                    var callArgs = request.Params?.Arguments is IReadOnlyDictionary<string, JsonElement> d
                        ? d
                        : FrozenDictionary<string, JsonElement>.Empty;
                    var result = await InvokeToolAsync(name, callArgs, cancellationToken).ConfigureAwait(false);
                    return ToCallToolResult(result);
                }
            }
        };

    internal void WireServer(McpServer server)
    {
        _serverRef = server;
        _hostDeps.ServerRef = server;
        CdpClientWorkspace.Wire(server);
        IdeDebugSaChannel.EnsureLiveLatchWired();
    }

    internal void NotifyListChanged()
    {
        _capabilitiesRevision.Bump();
        if (_serverRef is null) return;
        _ = _serverRef.SendNotificationAsync(
            NotificationMethods.ToolListChangedNotification,
            cancellationToken: CancellationToken.None);
    }

    internal async Task RunStdioAsync(CancellationToken cancellationToken = default)
    {
        await using var stdio = new StdioServerTransport("CdpMcp");
        await using var server = McpServer.Create(stdio, BuildMcpServerOptions());
        WireServer(server);
        Console.Error.WriteLine(
            $"CdpMcp {_mcpVersion} backends=[{string.Join(",", _byDomain.Keys)}] context={CdpEnumParse.ToWire(_session.Phase)}/{CdpEnumParse.ToWire(_session.Object)} isolation={CdpProfile.Kind}");
        await server.RunAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        _diskSyncWatch?.Dispose();
        _intercomCannon?.Dispose();
        _tenantRegistry.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal readonly record struct CdpInvokeResult(string Body, bool IsError);
