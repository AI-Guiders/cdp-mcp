#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>BuildAsync legacy tiles-desk compose peel.</summary>
internal static partial class IdeCockpit
{
    static async Task<string> BuildLegacyTilesDeskAsync(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        string mfd,
        object? goResult,
        object? warm,
        object[] next,
        object? focus,
        IdeAlertChannel.Snap alertSnap,
        List<Locus> loci,
        string[] goVerbs,
        BufferSnap buffer,
        string? focusId,
        Func<string, IReadOnlyDictionary<string, JsonElement>, CancellationToken, Task<string>> dispatch,
        CancellationToken cancellationToken)
    {
        object? tiles = null;
        var pins = SnapshotPins();
        var tileLayout = OptString(args, "layout");
        var requestPins = ResolveRequestedPins(args);
        var tilePins = requestPins.Count > 0 ? requestPins : pins;
        if (tilePins.Count > 0)
        {
            var fullPane = OptString(args, "pane_full") ?? OptString(args, "full_pane");
            tiles = await BuildTilesAsync(
                    tilePins, tileLayout, fullPane, args, buffer, focusId, dispatch, cancellationToken)
                .ConfigureAwait(false);
        }

        return ComposeTilesSurface(
            session, mfd, tiles, pins, goResult, warm, next, focus, alertSnap,
            loci, goVerbs, args, focusId);
    }
}
