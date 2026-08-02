#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent replace host-execute peel.</summary>
internal static partial class CitizenRouteHost
{
    static Applied ReplaceInPath(CitizenIntentRouter.Route route)
    {
        var path = route.Path;
        var oldString = route.OldString;
        if (string.IsNullOrWhiteSpace(path))
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "replace",
                Reason: "replace_path_empty");
        }

        if (string.IsNullOrEmpty(oldString))
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "replace",
                Path: path,
                Reason: "replace_old_empty");
        }

        var root = IdeCockpitHostChannel.ProjectRootResolver?.Invoke();
        if (!IdeLanguageTools.TryReplaceInDocument(
                path,
                root,
                oldString,
                route.NewString ?? "",
                out var full,
                out var docId,
                out var error))
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "replace",
                Path: path,
                Reason: error ?? "replace_failed");
        }

        var seat = IdeDeskSeats.PlaceOrgan("editor_scene");
        return new Applied(
            route.Raw,
            route.Verb.ToString(),
            Ok: true,
            Action: "replace",
            Seat: seat,
            Go: "editor_scene",
            Path: full,
            DocId: docId);
    }
}
