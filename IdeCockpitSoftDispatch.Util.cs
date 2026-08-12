#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.Cockpit.Surface;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

internal static partial class IdeCockpitSoftDispatch
{
    static readonly SoftInstrumentAliasCatalog SoftAliases = new();
    static readonly SoftInstrumentBoardMetaCatalog SoftMeta = new();

    static bool IsSoft(string? goVerb, SoftInstrumentKind kind) =>
        SoftAliases.TryResolve(goVerb) == kind;

    static void PlaceAndClear(ref string? goVerb, string organ)
    {
        // Soft organ / webcam desk pin ≠ human Face — do not steal browser show WebAi.
        if (IdeDeskSeats.IsSeatsMode())
            IdeDeskSeats.PlaceOrgan(organ, showFace: false);
        goVerb = null;
    }

    static void PlaceSoft(ref string? goVerb, SoftInstrumentKind kind) =>
        PlaceAndClear(ref goVerb, SoftMeta.Require(kind).Go);

    /// <summary>SoftDispatch → IdeSoftInstrumentBoard (lite bag; no seat extras).</summary>
        /// <summary>SoftDispatch → IdeSoftInstrumentBoard (lite bag; no seat extras).</summary>
    static object SoftBoard(
        SoftInstrumentKind kind,
        SessionContext session,
        DocumentBufferStore? docStore,
        IntentWorkspaceStore? workspaceStore,
        IntentWorkspaceState? workspaceState,
        IReadOnlyDictionary<string, JsonElement> args,
        string? goVerb = null,
        bool flattenOrganArgs = false,
        bool wantFull = false)
    {
        var tile = flattenOrganArgs
            ? new Dictionary<string, JsonElement>(OrganArgs(args), StringComparer.Ordinal)
            : new Dictionary<string, JsonElement>(args, StringComparer.Ordinal);
        var hit = new IdeSoftInstrumentBoard(new SoftInstrumentSeatBag(
            tile, session, docStore, workspaceStore, workspaceState, goVerb)).Build(kind);
        if (!wantFull)
            return hit.Board;
        return SeatInstrumentPanePresenter.Present(
            SoftMeta.Require(kind), wantFull: true, hit.Board, hit.Pulse, hit.Schema);
    }


    static string? OptString(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    /// <summary>IdeSaChannel reads depth/shape — not cockpit go_detail. Missing → pulse (anti-hang).</summary>
    static IReadOnlyDictionary<string, JsonElement> EnsureSaDeskDepth(
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var flat = new Dictionary<string, JsonElement>(OrganArgs(args), StringComparer.Ordinal);
        if (flat.ContainsKey("depth") || flat.ContainsKey("shape"))
            return flat;

        var fromDetail = OptString(flat, "go_detail");
        var depth = fromDetail?.Trim().ToLowerInvariant() switch
        {
            "full" or "raw" or "deep" => "full",
            "slim" or "a" or "desk" or "status" => "slim",
            _ => "pulse"
        };
        flat["depth"] = JsonSerializer.SerializeToElement(depth);
        return flat;
    }

    /// <summary>
    /// Cockpit passes nested <c>go_args</c>; soft organs expect flat organ keys (op/path/…).
    /// </summary>
    static IReadOnlyDictionary<string, JsonElement> OrganArgs(
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var flat = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var kv in args)
        {
            if (kv.Key.Equals("go_args", StringComparison.OrdinalIgnoreCase))
                continue;
            flat[kv.Key] = kv.Value;
        }

        if (args.TryGetValue("go_args", out var ga) && ga.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in ga.EnumerateObject())
                flat[p.Name] = p.Value.Clone();
        }

        return flat;
    }
}
