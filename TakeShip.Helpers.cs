using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>Helpers for TakeShip (≤ADX soft-warn peel).</summary>
internal static partial class TakeShip
{
    static object[] BuildTakeNext(bool plantLike, bool wantVision)
    {
        var list = new List<object>
        {
            new { go = "copy", label = "Copy to clipboard", why = "frame for paste elsewhere" },
            new { go = "put", label = "Put rewrite", why = "inverse — dump new draft" },
            new { go = "edit_draft", label = "Keep editing", why = "not done" }
        };
        if (plantLike && !wantVision)
        {
            list.Insert(0, new
            {
                go = "take",
                label = "See diagram",
                why = "vision=true — ImageContent for agent (opt-in)"
            });
        }

        return list.ToArray();
    }

    /// <summary>Sidecar PNG next to source so the agent can Read when she wants pixels.</summary>
    static string? TryWritePreviewPng(string sourcePath, byte[] png)
    {
        try
        {
            var dir = Path.GetDirectoryName(sourcePath);
            if (string.IsNullOrEmpty(dir))
                return null;
            Directory.CreateDirectory(dir);
            var path = Path.ChangeExtension(sourcePath, ".png");
            File.WriteAllBytes(path, png);
            return path;
        }
        catch
        {
            return null;
        }
    }

    static int CountLines(string text)
    {
        if (text.Length == 0) return 1;
        var n = 1;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n') n++;
        }

        return n;
    }

    static string DefaultVerifyScope(SessionContext session, DocBuffer buf)
    {
        var root = session.ProjectRoot;
        if (root is not { Length: > 0 } || session.SolutionOrProjectPath is not { Length: > 0 })
            return "syntax";

        try
        {
            var full = Path.GetFullPath(buf.Path);
            var rootFull = Path.GetFullPath(root);
            if (!rootFull.EndsWith(Path.DirectorySeparatorChar)
                && !rootFull.EndsWith(Path.AltDirectorySeparatorChar))
                rootFull += Path.DirectorySeparatorChar;

            if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                return "syntax";

            // Staging under .cdp/scratch = example on the knee — syntax only.
            var rel = Path.GetRelativePath(root, full);
            if (rel.StartsWith(".cdp" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || rel.StartsWith(".cdp" + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || string.Equals(rel, ".cdp", StringComparison.OrdinalIgnoreCase))
                return "syntax";

            return "project";
        }
        catch
        {
            return "syntax";
        }
    }

    static string? OptString(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.String)
            return null;
        var s = el.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    static bool BoolOr(IReadOnlyDictionary<string, JsonElement> args, string key, bool defaultValue)
    {
        if (!args.TryGetValue(key, out var el))
            return defaultValue;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(el.GetString(), out var b) => b,
            _ => defaultValue
        };
    }
}
