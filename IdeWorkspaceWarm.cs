using AIGuiders.Platform.Execution.Ide.Session;
using Cdp.Core;
using DotNetWorkspace.Core;
#if CDP_FEDERATION_IDE_SESSION
using AIGuiders.Platform.Modeling.Ide.Session;
#endif

namespace CdpMcp;

/// <summary>ADR-0062 — federation session warm on open (graph SSOT only; no bootstrap assets loader).</summary>
internal static class IdeWorkspaceWarm
{
    public static void WarmOnOpen(SessionContext session)
    {
#if CDP_FEDERATION_IDE_SESSION
        if (session.SolutionOrProjectPath is not { Length: > 0 } anchor)
            return;

        var pathCopy = anchor;
        _ = Task.Run(() =>
        {
            try
            {
                _ = FederationSessionRuntime.Open(pathCopy);
            }
            catch
            {
                // best-effort federation graph warm
            }
        });
#endif
    }

    /// <summary>Materialize CompilerServices from FTC workspace view before LRC dispatch.</summary>
    public static void MaterializeCompilerServices(FederationCompilerServicesEnsure? ensure)
    {
#if CDP_FEDERATION_IDE_SESSION
        if (ensure is not { Ok: true, WorkspaceView: { } view })
            return;

        if (!string.Equals(ensure.LanguageId, "fsharp", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            MsBuildLocatorOnce.EnsureRegistered();
            AIGuiders.Platform.Modeling.Language.Adapters.Fcs.FcsProjectOptions.invalidate();
            AIGuiders.Platform.Modeling.Language.Adapters.Fcs.FcsCompilerServicesHost.materialize(view);
        }
        catch
        {
            // ProjInfo warm is best-effort; SdkAssets fallback remains in adapter chain.
        }
#endif
    }
}
