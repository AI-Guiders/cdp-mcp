#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent delete|rm|remove host-execute peel (PathMutateGate).</summary>
internal static partial class CitizenRouteHost
{
    static Applied DeleteInPath(CitizenIntentRouter.Route route)
    {
        var path = route.Path;
        if (string.IsNullOrWhiteSpace(path))
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "delete",
                Reason: "delete_path_empty");
        }

        var force = string.Equals(route.Op, "force", StringComparison.OrdinalIgnoreCase);
        var root = IdeCockpitHostChannel.ProjectRootResolver?.Invoke();
        if (!IdeLanguageTools.TryDeleteDocument(
                path,
                root,
                force,
                out var full,
                out var error))
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "delete",
                Path: path,
                Reason: error ?? "delete_failed");
        }

        PublishGlassLandClose(full);
        return new Applied(
            route.Raw,
            route.Verb.ToString(),
            Ok: true,
            Action: "delete",
            Go: "buffer",
            Path: full);
    }

    /// <summary>Glass LatchHub land peel — close path after delete so projector drops partner invent.</summary>
    static void PublishGlassLandClose(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            return;
        NavigationLandLatch.Publish("close", fullPath, line: null, member: null, wire: null);
    }
}
