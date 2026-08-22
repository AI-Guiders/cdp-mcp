#nullable enable

namespace CdpMcp.Habitat;

/// <summary>Single SA/desk next hint row — data table cell, not IRule.</summary>
internal readonly record struct NextHint(string Go, string Label, string Why);

/// <summary>Verdict-keyed next[] tables for desk channels (Build/Debug/Test/pressure gate).</summary>
internal static class NextHintTable
{
    internal static object[] Resolve(
        string? verdict,
        IReadOnlyDictionary<string, NextHint[]> rows,
        NextHint[]? fallback = null,
        ReadOnlySpan<NextHint> prefix = default,
        ReadOnlySpan<NextHint> suffix = default)
    {
        var hints = verdict is not null && rows.TryGetValue(verdict, out var found)
            ? found
            : fallback ?? [];
        var list = new List<object>(prefix.Length + hints.Length + suffix.Length);
        Append(list, prefix);
        Append(list, hints);
        Append(list, suffix);
        return Dedup(list);
    }

    static void Append(List<object> list, ReadOnlySpan<NextHint> hints)
    {
        foreach (var h in hints)
            list.Add(ToObject(h));
    }

    static object ToObject(NextHint h) => new { go = h.Go, label = h.Label, why = h.Why };

    internal static object[] Dedup(IEnumerable<object> items)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<object>();
        foreach (var item in items)
        {
            var t = item.GetType();
            var key = (t.GetProperty("label")?.GetValue(item) as string ?? "") + "\0" +
                      (t.GetProperty("why")?.GetValue(item) as string ?? "");
            if (!seen.Add(key))
                continue;
            list.Add(item);
        }

        return list.ToArray();
    }
}
