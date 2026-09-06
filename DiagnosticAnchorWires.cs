using System.Text.Json;
using System.Text.Json.Nodes;

namespace CdpMcp;

/// <summary>
/// Diagnostics comfort parity (F# comfort = C# comfort): every diagnostic item carries a
/// BracketLocate anchor wire [F:rel;L:line] derived from its span — mutate-ready
/// (cdp_buffer op=edit anchor=). Best-effort: malformed payloads pass through unchanged.
/// </summary>
internal static class DiagnosticAnchorWires
{
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string Enrich(string rawDiagnosticsJson, string? projectRoot, string filePath)
    {
        try
        {
            if (JsonNode.Parse(rawDiagnosticsJson) is not JsonObject root ||
                root["diagnostics"] is not JsonArray items)
                return rawDiagnosticsJson;

            var rel = Rel(projectRoot, filePath);
            var changed = false;
            foreach (var item in items.OfType<JsonObject>())
            {
                if (item.ContainsKey("anchor"))
                    continue;
                if (item["span"] is not JsonObject span ||
                    span["line"] is not JsonNode lineNode ||
                    lineNode.GetValue<int>() <= 0)
                    continue;

                item["anchor"] = $"[F:{rel};L:{lineNode.GetValue<int>()}]";
                changed = true;
            }

            return changed ? root.ToJsonString(JsonOptions) : rawDiagnosticsJson;
        }
        catch
        {
            // Enrichment must never break diagnostics — pass raw through.
            return rawDiagnosticsJson;
        }
    }

    static string Rel(string? root, string abs)
    {
        // GUIDERS-ADR-0050: rel-under-workspace via PathBoundary; no hand-rolled TryRel.
        if (root is not { Length: > 0 })
            return AIGuiders.Platform.Modeling.Paths.LogicalPath.Normalize(abs);

        var logical = AIGuiders.Platform.Modeling.Paths.PathBoundary.ToLogical(root, abs);
        return logical.HasValue
            ? logical.Value.Value
            : AIGuiders.Platform.Modeling.Paths.LogicalPath.Normalize(abs);
    }
}