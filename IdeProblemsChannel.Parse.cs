#nullable enable
using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>Diag parse + arg helpers for go=problems.</summary>
internal static partial class IdeProblemsChannel
{
    sealed record ParsedItem(
        string Severity,
        string Message,
        string? Code,
        int Line,
        int EndLine,
        string? Anchor);

    static bool TryParseItems(
        string json,
        string bufferPath,
        string? projectRoot,
        out List<ParsedItem> items)
    {
        items = [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            foreach (var arr in FindItemArrays(root))
            {
                foreach (var el in arr.EnumerateArray())
                    if (TryMapItem(el, bufferPath, projectRoot, out var item))
                        items.Add(item);
            }

            return items.Count > 0 || root.ValueKind == JsonValueKind.Object;
        }
        catch
        {
            return false;
        }
    }

}
