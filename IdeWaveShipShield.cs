#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// Wave shipped teeth — no auto-complete theatre; human-faced waves need PNG + domain stamp.
/// Parity: <see cref="IdeHumanFaceShield"/> · <see cref="IdeDomainStampShield"/> · playbook-being-vs-seeming.
/// </summary>
internal static class IdeWaveShipShield
{
    internal const string RefusePendingId = "wave_ship_pending_items";

    internal static bool TryRefuse(
        IdeWaveChannel.WaveDoc doc,
        IReadOnlyDictionary<string, JsonElement>? args,
        out string error,
        out string hint)
    {
        error = "";
        hint = "";
        try
        {
            RefuseShipWithoutTeeth(doc, args);
            return false;
        }
        catch (ArgumentException ex)
        {
            error = ex.Message.Contains("refused —", StringComparison.Ordinal)
                ? ex.Message.Split("refused —", 2, StringSplitOptions.None)[1].TrimStart()
                : ex.Message;
            hint = error.Contains(RefusePendingId, StringComparison.Ordinal)
                ? "Mark each wave item done (wave item done <label>), then wave shipped evidence=….png domain=glass. force=true escape."
                : "Human-faced wave: evidence=path.png on disk + domain= with fresh ## last_ship. force=true escape.";
            return true;
        }
    }

    internal static void RefuseShipWithoutTeeth(
        IdeWaveChannel.WaveDoc doc,
        IReadOnlyDictionary<string, JsonElement>? args)
    {
        if (ForceArg(args))
            return;

        var pending = doc.Items.Where(i => !string.Equals(i.Status, "done", StringComparison.OrdinalIgnoreCase))
            .Select(i => i.Label)
            .ToList();
        if (pending.Count > 0)
        {
            throw new ArgumentException(
                $"wave shipped refused — {RefusePendingId}: {pending.Count} item(s) still pending " +
                $"({string.Join(", ", pending.Take(4))}{(pending.Count > 4 ? "…" : "")}). " +
                "Mark each item done first — wave shipped must not auto-complete the rectangle.");
        }

        IdeSeemingDoneShield.RefuseHumanFaceShipWithoutTeeth(args, IdeSeemingDoneShield.WaveBlob(doc), "wave shipped");
    }

    static bool ForceArg(IReadOnlyDictionary<string, JsonElement>? args)
    {
        if (args is null)
            return false;
        if (args.TryGetValue("force", out var el))
        {
            if (el.ValueKind == JsonValueKind.True)
                return true;
            if (el.ValueKind == JsonValueKind.String && bool.TryParse(el.GetString(), out var b) && b)
                return true;
        }

        if (args.TryGetValue("go_args", out var ga) && ga.ValueKind == JsonValueKind.Object
            && ga.TryGetProperty("force", out var gf))
        {
            if (gf.ValueKind == JsonValueKind.True)
                return true;
            if (gf.ValueKind == JsonValueKind.String && bool.TryParse(gf.GetString(), out var gb) && gb)
                return true;
        }

        return false;
    }
}
