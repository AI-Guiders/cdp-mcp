#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.Backends;

namespace CdpMcp;

/// <summary>CallTool router peeled from Program.DispatchAsync (soft-warn).</summary>
internal static class IdeToolDispatch
{
    public static async Task<string> DispatchAsync(IdeToolDispatchDeps d, string name,
        IReadOnlyDictionary<string, JsonElement> callArgs, CancellationToken cancellationToken)
    {
        var session = d.Session;
        var docStore = d.DocStore;
        var byDomain = d.ByDomain;
        var settings = d.Settings;
        var shellHabitat = d.ShellHabitat;
        var NotifyListChanged = d.NotifyListChanged;
        var DispatchMetaAsync = d.DispatchMetaAsync;

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
                    (n, a, ct) => DispatchMetaAsync(n, a, ct, null),
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
}

internal sealed class IdeToolDispatchDeps
{
    public required SessionContext Session { get; init; }
    public required DocumentBufferStore DocStore { get; init; }
    public required IReadOnlyDictionary<string, ICdpBackendModule> ByDomain { get; init; }
    public required CdpSettings Settings { get; init; }
    public required TerminalMcp.Core.ShellHabitat ShellHabitat { get; init; }
    public required Action NotifyListChanged { get; init; }
    public required Func<string, IReadOnlyDictionary<string, JsonElement>, CancellationToken, object?, Task<string>> DispatchMetaAsync { get; init; }
}
