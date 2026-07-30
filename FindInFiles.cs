using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>
/// VS Find in Files — corpus text search via ripgrep, results as anchors.
/// Called from EditorComfort find when scope=project|files|external|… (regex= is Use Regular Expressions).
/// </summary>
internal static class FindInFiles
{
    public const int DefaultMax = 40;
    public const int HardMax = 200;
    public const int TimeoutMs = 20_000;
    public const int ExternalTimeoutMs = 60_000;

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    static readonly HashSet<string> SkipGlobs = new(StringComparer.OrdinalIgnoreCase)
    {
        "!**/bin/**", "!**/obj/**", "!**/.git/**", "!**/.vs/**",
        "!**/node_modules/**", "!**/packages/**", "!**/TestResults/**",
        "!**/publish/**", "!**/publish-release/**", "!**/dist/**"
    };

    public static bool IsExternalScope(string? scope)
    {
        var s = (scope ?? "").Trim().ToLowerInvariant();
        return s is "external" or "disk" or "system" or "fs" or "anywhere";
    }

    public static bool IsFilesScope(string? scope)
    {
        var s = (scope ?? "").Trim().ToLowerInvariant();
        return s is "project" or "files" or "solution" or "workspace" or "all" or "repo"
            || IsExternalScope(s);
    }

    /// <summary>Optional multi-root / file list (dirty, buffers, roots[]). When set, overrides single searchRoot as rg paths.</summary>
    public static List<string>? OptPaths(IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!args.TryGetValue("paths", out var el) && !args.TryGetValue("roots", out el))
            return null;

        var list = new List<string>();
        if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } s)
                    list.Add(Path.GetFullPath(s.Trim()));
            }
        }
        else if (el.ValueKind == JsonValueKind.String && el.GetString() is { Length: > 0 } csv)
        {
            foreach (var part in csv.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                list.Add(Path.GetFullPath(part));
        }

        return list.Count > 0 ? list : null;
    }

    public static string Dispatch(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        bool all)
    {
        var scopeRaw = Opt(args, "scope") ?? Opt(args, "in") ?? "project";
        var external = IsExternalScope(scopeRaw);
        var scopeWire = external ? "external" : "project";

        var query = Opt(args, "query") ?? Opt(args, "text") ?? Opt(args, "pattern");
        if (string.IsNullOrEmpty(query))
        {
            return JsonSerializer.Serialize(new
            {
                schema = EditorComfort.Schema,
                ok = false,
                op = all ? "find_all" : "find",
                scope = scopeWire,
                error = "query_required",
                hint = "query= + scope=project|external. regex=true = Use Regular Expressions."
            }, Pretty);
        }

        var multiPaths = OptPaths(args);
        string searchRoot;
        string cwd;
        if (multiPaths is { Count: > 0 })
        {
            searchRoot = multiPaths[0];
            cwd = Directory.Exists(searchRoot)
                ? searchRoot
                : (Path.GetDirectoryName(searchRoot)
                   ?? session.ProjectRoot
                   ?? Environment.CurrentDirectory);
            if (!external && session.ProjectRoot is { Length: > 0 } && Directory.Exists(session.ProjectRoot))
                cwd = session.ProjectRoot!;
        }
        else if (!TryResolveSearchRoot(session, args, external, out searchRoot, out cwd, out var rootError, out var rootHint))
        {
            return JsonSerializer.Serialize(new
            {
                schema = EditorComfort.Schema,
                ok = false,
                op = all ? "find_all" : "find",
                scope = scopeWire,
                error = rootError,
                hint = rootHint
            }, Pretty);
        }

        var rg = ResolveRg();
        if (rg is null)
        {
            return JsonSerializer.Serialize(new
            {
                schema = EditorComfort.Schema,
                ok = false,
                op = all ? "find_all" : "find",
                scope = scopeWire,
                error = "rg_not_found",
                hint = "Install ripgrep on PATH, or set env CDP_RG to rg.exe"
            }, Pretty);
        }

        var regex = BoolOr(args, "regex", false);
        var ignoreCase = BoolOr(args, "ignore_case", true);
        var max = Math.Clamp(
            IntOr(args, "max", all ? EditorComfort.MaxFindHits : DefaultMax),
            1,
            HardMax);

        var glob = Opt(args, "glob") ?? Opt(args, "g");
        var volumeProbe = multiPaths is { Count: > 0 } ? multiPaths[0] : searchRoot;
        if (external && IsVolumeRoot(volumeProbe) && glob is not { Length: > 0 } && multiPaths is not { Count: > 1 })
        {
            return JsonSerializer.Serialize(new
            {
                schema = EditorComfort.Schema,
                ok = false,
                op = all ? "find_all" : "find",
                scope = scopeWire,
                error = "glob_required_for_volume_root",
                path = volumeProbe,
                hint = "Volume root search needs glob= (e.g. *.cs) or a narrower path=."
            }, Pretty);
        }

        var argv = new List<string>
        {
            "--json",
            "--color", "never",
            // Per-file cap (rg); global cap applied while parsing.
            "--max-count", "5"
        };
        if (ignoreCase)
            argv.Add("-i");
        if (!regex)
            argv.Add("-F");

        foreach (var g in SkipGlobs)
            argv.AddRange(["--glob", g]);

        if (glob is { Length: > 0 })
            argv.AddRange(["--glob", glob]);

        var type = Opt(args, "type") ?? Opt(args, "filetype");
        if (type is { Length: > 0 })
            argv.AddRange(["--type", type]);

        argv.Add("--");
        argv.Add(query!);
        if (multiPaths is { Count: > 0 })
            argv.AddRange(multiPaths);
        else
            argv.Add(searchRoot);

        var timeout = external ? ExternalTimeoutMs : TimeoutMs;
        if (!TryRunRg(rg, argv, cwd, timeout, out var stdout, out var stderr, out var exit, out var runError))
        {
            return JsonSerializer.Serialize(new
            {
                schema = EditorComfort.Schema,
                ok = false,
                op = all ? "find_all" : "find",
                scope = scopeWire,
                error = "rg_failed",
                detail = runError,
                hint = "Check CDP_RG / PATH and query (regex syntax)."
            }, Pretty);
        }

        // rg: 0 = matches, 1 = no matches, 2 = error
        if (exit >= 2)
        {
            return JsonSerializer.Serialize(new
            {
                schema = EditorComfort.Schema,
                ok = false,
                op = all ? "find_all" : "find",
                scope = scopeWire,
                error = "rg_exit",
                exit_code = exit,
                stderr = Trim(stderr, 800),
                hint = "Bad regex or path? Try regex=false or narrower glob="
            }, Pretty);
        }

        var hits = ParseJsonHits(session, stdout, max);
        if (!all && hits.Count > 1)
            hits = hits.Take(1).ToList();

        object? land = null;
        if (hits.Count > 0)
        {
            EditorComfort.PushLocus(session, hits[0].Anchor);
            if (BoolOr(args, "peek", true))
                land = TryLand(store, session, hits[0]);
        }

        return JsonSerializer.Serialize(new
        {
            schema = EditorComfort.Schema,
            ok = true,
            op = all ? "find_all" : "find",
            scope = scopeWire,
            path = searchRoot,
            query,
            regex,
            ignore_case = ignoreCase,
            engine = "rg",
            rg_path = rg,
            count = hits.Count,
            truncated = hits.Count >= max,
            hits = hits.Select(h => new
            {
                anchor = h.Anchor,
                path = h.AbsolutePath,
                line = h.Line,
                column = h.Column,
                preview = h.Preview
            }),
            land,
            next = hits.Count > 0
                ? (object[])
                [
                    new { go = "complete", label = "Completions at hit", why = "line/column from hits[0]" },
                    new { go = "signature_help", label = "Signature help", why = "near hit" },
                    new { go = "scope", label = "Sniper from land", why = $"from={hits[0].Anchor}" },
                    new { go = "edit_draft", label = "Edit here", why = "land open+peeked" },
                    new { go = "find_all", label = "More hits", why = $"same query + scope={scopeWire}" }
                ]
                : (object[])
                [
                    new { go = "find", label = "Retry", why = $"regex= / glob= / scope={scopeWire} / path=" }
                ],
            hint =
                "VS Find in Files. scope=project|files|external → rg → anchors. " +
                "external requires path= (any disk tree; no cdp_open). " +
                "regex=true = Use Regular Expressions. find = first; find_all = capped list."
        }, Pretty);
    }

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
