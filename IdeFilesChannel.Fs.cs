#nullable enable
using System.Text.Json;
using System.Text.RegularExpressions;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Board/Enumerate/WalkTree/ResolveCwd helpers for IdeFilesChannel (soft-warn peel).</summary>
internal static partial class IdeFilesChannel
{
    static object Board(
        string op,
        string where,
        string cwd,
        string shape,
        IReadOnlyList<object> entries,
        int total,
        bool truncated,
        string? hint)
    {
        var pulse = $"files · {where} · {ShortPath(cwd)} · {total}";
        CideFilesDeskLatch.Publish(
            active: true,
            pulse: pulse,
            op: op,
            where: where,
            cwd: cwd,
            entryCount: total);
        return new
        {
            ok = true,
            schema = SchemaVersion,
            role = "files",
            go = "files_desk",
            tool = ToolName,
            op,
            where,
            cwd,
            shape,
            pulse,
            total,
            truncated,
            entries,
            next = Next(cwd),
            hint
        };
    }

    static object[] Next(string cwd) =>
    [
        new { go = "files_desk", label = "Up", why = "op=up" },
        new { go = "files_desk", label = "List", why = "op=list" },
        new { go = "files_desk", label = "Tree", why = "op=tree depth=2" },
        new { go = "files_desk", label = "Search here", why = $"op=search path={cwd} query=" },
        new { go = "find_desk", label = "Find desk", why = $"where=external path={cwd}" }
    ];

}
