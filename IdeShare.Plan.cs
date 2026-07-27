#nullable enable
using System.Text.Json;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

internal static partial class IdeShare
{
    /// <summary>Wrap plan promote as share with=operator what=plan.</summary>
    public static object SharePlan(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        string? projectRoot,
        string? notes,
        string? dirOverride,
        string? ask)
    {
        var askNorm = NormalizeAsk(ask);
        if (askNorm is "none")
            askNorm = "confirm";
        var promoted = IdePlanPromote.Promote(store, state, projectRoot, notes, dirOverride);
        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "share",
            with = "operator",
            what = "plan",
            ask = askNorm,
            alias_of = "promote",
            result = promoted,
            chat = ExtractChat(promoted),
            hint =
                "share with=operator what=plan ask=confirm (alias: promote). " +
                "Human reads path; agent relays chat= only — do not paste plan body."
        };
    }

    static string? ExtractChat(object promoted)
    {
        try
        {
            var json = JsonSerializer.Serialize(promoted);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("chat", out var c) ? c.GetString() : null;
        }
        catch
        {
            return null;
        }
    }
}
