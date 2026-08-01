using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>Search root resolve, rg runner, hit parse for Find in Files.</summary>
internal static partial class FindInFiles
{
    static bool TryResolveSearchRoot(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        bool external,
        out string searchRoot,
        out string cwd,
        out string error,
        out string hint)
    {
        searchRoot = "";
        cwd = "";
        error = "";
        hint = "";

        var pathArg = Opt(args, "path") ?? Opt(args, "search_in") ?? Opt(args, "root");

        if (external)
        {
            if (pathArg is not { Length: > 0 })
            {
                error = "path_required";
                hint = "scope=external needs path= absolute dir/file (or ~). Prefer narrower than volume root; else glob=.";
                return false;
            }

            searchRoot = ExpandPath(pathArg);
            if (!Path.IsPathRooted(searchRoot))
            {
                error = "path_not_rooted";
                hint = "scope=external path= must be absolute (e.g. D:\\Experiments\\agent-notes).";
                return false;
            }

            if (!Directory.Exists(searchRoot) && !File.Exists(searchRoot))
            {
                error = "path_not_found";
                hint = $"path= not found: {searchRoot}";
                return false;
            }

            cwd = Directory.Exists(searchRoot)
                ? searchRoot
                : (Path.GetDirectoryName(searchRoot) ?? searchRoot);
            return true;
        }

        var root = session.ProjectRoot;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            error = "no_project";
            hint = "cdp_open first — or use scope=external path= for disk-wide find";
            return false;
        }

        searchRoot = pathArg ?? root!;
        if (!Path.IsPathRooted(searchRoot))
            searchRoot = Path.GetFullPath(Path.Combine(root!, searchRoot));
        else
            searchRoot = Path.GetFullPath(searchRoot);

        if (!Directory.Exists(searchRoot) && !File.Exists(searchRoot))
            searchRoot = root!;

        cwd = root!;
        return true;
    }

    static string ExpandPath(string raw)
    {
        var s = raw.Trim().Trim('"');
        if (s.StartsWith("~/", StringComparison.Ordinal) || s.StartsWith("~\\", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            s = Path.Combine(home, s[2..]);
        }
        else if (s == "~")
        {
            s = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        return Path.GetFullPath(s);
    }

    static bool IsVolumeRoot(string path)
    {
        try
        {
            var full = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var root = Path.GetPathRoot(full)?
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return root is { Length: > 0 } &&
                   full.Equals(root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    static List<Hit> ParseJsonHits(SessionContext session, string stdout, int max)
    {
        var list = new List<Hit>();
        using var reader = new StringReader(stdout);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || list.Count >= max)
                continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var typeEl) ||
                    typeEl.GetString() is not "match")
                    continue;
                if (!root.TryGetProperty("data", out var data))
                    continue;
                var path = data.GetProperty("path").GetProperty("text").GetString();
                if (string.IsNullOrEmpty(path))
                    continue;
                var lineNum = data.GetProperty("line_number").GetInt32();
                var abs = Path.GetFullPath(path);
                var label = FileLabel(session, abs);
                var preview = "";
                var col = 1;
                if (data.TryGetProperty("lines", out var lines) &&
                    lines.TryGetProperty("text", out var textEl))
                {
                    preview = (textEl.GetString() ?? "").TrimEnd('\r', '\n');
                    if (preview.Length > 80)
                        preview = preview[..80] + "…";
                    preview = preview.Replace("\r", "").Replace("\n", "⏎");
                }

                if (data.TryGetProperty("submatches", out var subs) &&
                    subs.ValueKind == JsonValueKind.Array &&
                    subs.GetArrayLength() > 0 &&
                    subs[0].TryGetProperty("start", out var startEl))
                {
                    col = startEl.GetInt32() + 1;
                }

                var needle = BracketLocate.SanitizeTextNeedle(preview);
                var anchor = string.IsNullOrWhiteSpace(needle)
                    ? BracketLocate.Format(new BracketLocate.Span(label, null, lineNum, null))
                    : BracketLocate.Format(new BracketLocate.Span(label, null, lineNum, null, TextNeedle: needle));
                list.Add(new Hit(anchor, abs, lineNum, col, preview));
            }
            catch
            {
                // skip malformed json line
            }
        }

        return list;
    }

    static object? TryLand(DocumentBufferStore store, SessionContext session, Hit top)
    {
        try
        {
            if (!File.Exists(top.AbsolutePath))
                return null;
            var buf = store.Open(top.AbsolutePath);
            EditorComfort.RememberFile(top.AbsolutePath);
            var lines = SplitLines(buf.Text);
            var pad = 2;
            var start = Math.Max(1, top.Line - pad);
            var end = Math.Min(lines.Count, top.Line + pad);
            var slice = string.Join('\n', lines.Skip(start - 1).Take(end - start + 1));
            if (slice.Length > 2_400)
                slice = slice[..2_400] + "\n…";
            return new
            {
                anchor = top.Anchor,
                doc_id = buf.DocId,
                start_line = start,
                end_line = end,
                text = slice
            };
        }
        catch
        {
            return null;
        }
    }

    static bool TryRunRg(
        string rg,
        List<string> argv,
        string cwd,
        int timeoutMs,
        out string stdout,
        out string stderr,
        out int exit,
        out string? error)
    {
        stdout = "";
        stderr = "";
        exit = -1;
        error = null;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = rg,
                WorkingDirectory = cwd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var a in argv)
                psi.ArgumentList.Add(a);

            using var p = Process.Start(psi);
            if (p is null)
            {
                error = "process_start_null";
                return false;
            }

            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
                error = "timeout";
                return false;
            }

            stdout = outTask.GetAwaiter().GetResult();
            stderr = errTask.GetAwaiter().GetResult();
            exit = p.ExitCode;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    static string? ResolveRg()
    {
        var env = Environment.GetEnvironmentVariable("CDP_RG");
        if (env is { Length: > 0 } && File.Exists(env))
            return env;

        // Prefer PATH resolution via where/where.exe semantics — try common names.
        foreach (var name in new[] { "rg.exe", "rg" })
        {
            var hit = FindOnPath(name);
            if (hit is not null)
                return hit;
        }

        return null;
    }

    static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), fileName);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch
            {
                // ignore bad PATH entries
            }
        }

        return null;
    }

    static string FileLabel(SessionContext session, string absolutePath)
    {
        var root = session.ProjectRoot;
        if (root is { Length: > 0 })
        {
            var fullRoot = Path.GetFullPath(root);
            var full = Path.GetFullPath(absolutePath);
            if (full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                var rel = full[fullRoot.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (rel.Length > 0)
                    return rel.Replace('\\', '/');
            }
        }

        // Outside project: keep rooted path so anchors stay unique (F: value may contain drive ':').
        return Path.GetFullPath(absolutePath).Replace('\\', '/');
    }

    static List<string> SplitLines(string text)
    {
        var list = new List<string>();
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
            list.Add(line);
        return list;
    }

    static string Trim(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.String)
            return null;
        var s = el.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    static bool BoolOr(IReadOnlyDictionary<string, JsonElement> args, string key, bool defaultValue)
    {
        if (!args.TryGetValue(key, out var el))
            return defaultValue;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => defaultValue
        };
    }

    static int IntOr(IReadOnlyDictionary<string, JsonElement> args, string key, int defaultValue)
    {
        if (!args.TryGetValue(key, out var el))
            return defaultValue;
        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(el.GetString(), out var n) => n,
            _ => defaultValue
        };
    }

    sealed record Hit(string Anchor, string AbsolutePath, int Line, int Column, string Preview);
}
