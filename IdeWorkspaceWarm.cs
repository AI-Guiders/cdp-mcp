using AIGuiders.Platform.Modeling.Language.Adapters.Fcs;
using Cdp.Core;
using DotNetWorkspace.Core;
#if CDP_FEDERATION_IDE_SESSION
using AIGuiders.Platform.Execution.Ide.Session;
#endif

namespace CdpMcp;

/// <summary>Phased workspace warm on session open (graph → restore → build → compile context).</summary>
internal static class IdeWorkspaceWarm
{
    public static void WarmOnOpen(SessionContext session)
    {
        if (session.SolutionOrProjectPath is not { Length: > 0 } anchor)
            return;

        var pathCopy = anchor;
        _ = Task.Run(() =>
        {
            try
            {
                WorkspaceProjectWarm.WarmSolution(
                    pathCopy,
                    DotNetProjectKind.FSharp,
                    WorkspaceProjectWarm.FSharpWarmOptions);
            }
            catch
            {
                // best-effort
            }

#if CDP_FEDERATION_IDE_SESSION
            try
            {
                _ = FederationSessionRuntime.Open(pathCopy);
            }
            catch
            {
                // best-effort federation graph warm
            }
#endif

            try
            {
                var graph = global::DotNetWorkspace.Core.DotNetWorkspace.Load(pathCopy);
                foreach (var project in graph.Projects.Where(p => p.Kind == DotNetProjectKind.FSharp))
                    FcsProjectOptions.warm(project.AbsolutePath);
            }
            catch
            {
                // best-effort
            }
        });
    }

    /// <summary>Sync F# project-options warm before LRC dispatch (async WarmOnOpen may still be in flight).</summary>
    public static void WarmFsharpFileOnLrc(string filePath, string? anchorPath)
    {
        if (string.IsNullOrWhiteSpace(anchorPath)
            || !filePath.EndsWith(".fs", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            var entry = global::DotNetWorkspace.Core.DotNetWorkspace.TryResolveOwningProject(
                filePath,
                anchorPath,
                DotNetProjectKind.FSharp);
            if (entry?.AbsolutePath is { Length: > 0 } fsproj)
                FcsProjectOptions.warm(fsproj);
        }
        catch
        {
            // best-effort
        }
    }
}
