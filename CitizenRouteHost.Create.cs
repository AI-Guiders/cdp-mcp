#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent create|write host-execute peel (PathMutateGate).</summary>
internal static partial class CitizenRouteHost
{
    static Applied CreateInPath(CitizenIntentRouter.Route route)
    {
        var path = route.Path;
        if (string.IsNullOrWhiteSpace(path))
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "create",
                Reason: "create_path_empty");
        }

        var overwrite = string.Equals(route.Op, "overwrite", StringComparison.OrdinalIgnoreCase);
        var root = IdeCockpitHostChannel.ProjectRootResolver?.Invoke();
        if (!IdeLanguageTools.TryCreateDocument(
                path,
                root,
                route.NewString ?? "",
                overwrite,
                out var full,
                out var docId,
                out var error))
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "create",
                Path: path,
                Reason: error ?? "create_failed");
        }

        var seat = IdeDeskSeats.PlaceOrgan("editor_scene");
        PublishGlassLandOpen(full);
        return new Applied(
            route.Raw,
            route.Verb.ToString(),
            Ok: true,
            Action: "create",
            Seat: seat,
            Go: "editor_scene",
            Path: full,
            DocId: docId);
    }
}
