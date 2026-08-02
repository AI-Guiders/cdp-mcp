#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent append host-execute peel (PathMutateGate).</summary>
internal static partial class CitizenRouteHost
{
    static Applied AppendInPath(CitizenIntentRouter.Route route)
    {
        var path = route.Path;
        if (string.IsNullOrWhiteSpace(path))
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "append",
                Reason: "append_path_empty");
        }

        if (string.IsNullOrEmpty(route.NewString))
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "append",
                Path: path,
                Reason: "append_body_empty");
        }

        var root = IdeCockpitHostChannel.ProjectRootResolver?.Invoke();
        if (!IdeLanguageTools.TryAppendDocument(
                path,
                root,
                route.NewString,
                out var full,
                out var docId,
                out var error))
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "append",
                Path: path,
                Reason: error ?? "append_failed");
        }

        var seat = IdeDeskSeats.PlaceOrgan("editor_scene");
        PublishGlassLandOpen(full);
        return new Applied(
            route.Raw,
            route.Verb.ToString(),
            Ok: true,
            Action: "append",
            Seat: seat,
            Go: "editor_scene",
            Path: full,
            DocId: docId);
    }
}
