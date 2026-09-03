using AIGuiders.Platform.Execution.Language;
using Cdp.Core;

#if CDP_FEDERATION_IDE_SESSION
using AIGuiders.Platform.Execution.Ide.Session;
#endif

namespace CdpMcp;

/// <summary>ADR-0062 topology gate: ensure CompilerServices, then dispatch in-proc via LRC center (OOP worker = future leaf).</summary>
internal static class CdpLanguageResolverGate
{
    const string OutOfProcessTopology = "out-of-process";

    public static async Task<object?> DispatchAsync(
        string bareVerb,
        SessionContext session,
        LanguageRequest request,
        RenameSymbolRequest? renameRequest,
        CancellationToken cancellationToken)
    {
        var ensure = EnsureCompilerServices(session, request.FilePath);
        RefuseOutOfProcess(ensure);

        var center = CdpLanguageResolverHost.Center;
        return bareVerb switch
        {
            "get_diagnostics" =>
                await center.DispatchDiagnosticsAsync(request, cancellationToken).ConfigureAwait(false),
            "get_document_symbols" =>
                await center.DispatchDocumentSymbolsAsync(request, cancellationToken).ConfigureAwait(false),
            "go_to_definition" =>
                await center.DispatchGoToDefinitionAsync(request, cancellationToken).ConfigureAwait(false),
            "find_usages" =>
                await center.DispatchFindUsagesAsync(request, cancellationToken).ConfigureAwait(false),
            "get_completions" =>
                await center.DispatchCompletionsAsync(request, cancellationToken).ConfigureAwait(false),
            "get_symbol_at_position" =>
                await center.DispatchSymbolAtPositionAsync(request, cancellationToken).ConfigureAwait(false),
            "rename_symbol" when renameRequest is not null =>
                await center.DispatchRenameSymbolAsync(renameRequest, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentException($"Unsupported LRC verb: {bareVerb}"),
        };
    }

#if CDP_FEDERATION_IDE_SESSION
    static FederationCompilerServicesEnsure? EnsureCompilerServices(SessionContext session, string filePath)
    {
        if (session.SolutionOrProjectPath is not { Length: > 0 } anchor)
            return null;

        return FederationSessionRuntime.TryEnsureCompilerServices(anchor, filePath);
    }
#else
    static FederationCompilerServicesEnsure? EnsureCompilerServices(SessionContext _, string __) => null;
#endif

    static void RefuseOutOfProcess(FederationCompilerServicesEnsure? ensure)
    {
        if (ensure is not { Ok: true, Topology: OutOfProcessTopology })
            return;

        throw new NotSupportedException(
            "CompilerServices topology is out-of-process for this project; language worker host is not configured in CDP v1. " +
            "Use in-process capability attributes or StaticAnalysis adaptive rules (ADR-0062).");
    }
}
