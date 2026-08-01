#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Auto group inference from package.json (≤ADX soft-warn peel).</summary>
internal static partial class CdpPluginQuarantine
{
    static List<string> InferAutoGroups(JsonElement pkg, bool hasPayload, string id, string displayName)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? raw)
        {
            var g = NormalizeGroupId(raw);
            if (g.Length > 0)
                set.Add(g);
        }

        if (pkg.TryGetProperty("categories", out var cats) && cats.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in cats.EnumerateArray())
                Add(c.GetString());
        }

        if (pkg.TryGetProperty("keywords", out var keys) && keys.ValueKind == JsonValueKind.Array)
        {
            foreach (var k in keys.EnumerateArray())
            {
                var s = k.GetString();
                if (s is null) continue;
                // Only short topical keywords — skip sentences.
                if (s.Length <= 24 && !s.Contains(' '))
                    Add(s);
            }
        }

        if (pkg.TryGetProperty("contributes", out var contrib) && contrib.ValueKind == JsonValueKind.Object
            && contrib.TryGetProperty("languages", out var langs) && langs.ValueKind == JsonValueKind.Array)
        {
            foreach (var lang in langs.EnumerateArray())
            {
                var lid = Prop(lang, "id");
                if (lid is { Length: > 0 })
                    Add("lang-" + lid);
            }
        }

        var blob = (id + " " + displayName).ToLowerInvariant();
        if (blob.Contains("plantuml") || blob.Contains("uml") || hasPayload && blob.Contains("plant"))
            Add("diagrams");
        if (blob.Contains("javascript") || blob.Contains("typescript") || set.Contains("javascript") || set.Contains("typescript"))
            Add("javascript");
        if (blob.Contains("python"))
            Add("python");
        if (blob.Contains("markdown") || set.Contains("markdown"))
            Add("markdown");

        // Map common VS Code categories to short group ids
        if (set.Contains("programming-languages"))
        {
            set.Remove("programming-languages");
            // keep lang-* if any
        }

        if (set.Count == 0)
            set.Add("ungrouped");

        return set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
