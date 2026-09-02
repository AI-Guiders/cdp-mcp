using AIGuiders.Platform.Modeling.Language.Adapters.Fcs;
using Cdp.Core;
using DotNetWorkspace.Core;

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
}
