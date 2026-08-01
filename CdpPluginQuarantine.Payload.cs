#nullable enable
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CdpMcp;

internal static partial class CdpPluginQuarantine
{
    static ModeAPayload? FindModeAPayload(string pluginRoot, string extensionDir)
    {
        var all = FindAllModeAPayloads(pluginRoot, extensionDir);
        return all.Count > 0 ? all[0] : null;
    }

    /// <summary>Recursive scan; best tool payload first (not dependency jars).</summary>
    static List<ModeAPayload> FindAllModeAPayloads(string pluginRoot, string extensionDir)
    {
        var scored = new List<(int Score, ModeAPayload Payload)>();
        if (!Directory.Exists(extensionDir))
            return [];

        foreach (var file in EnumeratePayloadCandidateFiles(extensionDir))
        {
            if (!TryScorePayload(file, out var kind, out var score))
                continue;
            var rel = Path.GetRelativePath(pluginRoot, file).Replace('\\', '/');
            scored.Add((score, new ModeAPayload(kind, rel, file)));
        }

        return scored
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Payload.RelPath.Length)
            .Select(x => x.Payload)
            .ToList();
    }

    static IEnumerable<string> EnumeratePayloadCandidateFiles(string extensionDir)
    {
        var skipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules", ".git", "out", "test", "tests", "__tests__", "fixtures", ".vs", "obj"
        };

        var stack = new Stack<(string Dir, int Depth)>();
        stack.Push((extensionDir, 0));
        while (stack.Count > 0)
        {
            var (dir, depth) = stack.Pop();
            if (depth > 10)
                continue;

            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch { continue; }

            foreach (var f in files)
                yield return f;

            string[] subs;
            try { subs = Directory.GetDirectories(dir); }
            catch { continue; }

            foreach (var sub in subs)
            {
                var name = Path.GetFileName(sub);
                if (skipDirs.Contains(name))
                    continue;
                stack.Push((sub, depth + 1));
            }
        }
    }

    static bool TryScorePayload(string file, out string kind, out int score)
    {
        kind = "";
        score = 0;
        var ext = Path.GetExtension(file);
        var name = Path.GetFileName(file);
        var parent = Path.GetFileName(Path.GetDirectoryName(file) ?? "");
        var inBinish = parent is "bin" or "tools" or "native" or "binaries" or "runtimes";
        var underLib = file.Contains($"{Path.DirectorySeparatorChar}lib{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                       || parent.Equals("lib", StringComparison.OrdinalIgnoreCase);

        if (ext.Equals(".jar", StringComparison.OrdinalIgnoreCase))
        {
            if (IsDependencyJarName(name))
                return false;

            kind = "jar";
            var n = name.ToLowerInvariant();
            if (n.Contains("plantuml")) score = 120;
            else if (n.EndsWith("-all.jar") || n.Contains("-all-")) score = 115;
            else if (n.Contains("cli")) score = 112;
            else if (n is "spotbugs.jar" or "checkstyle.jar" or "pmd.jar") score = 110;
            else if (n.StartsWith("checkstyle") || n.StartsWith("pmd-") || n.StartsWith("spotbugs"))
                score = underLib ? 100 : 108;
            else if (n.Contains("plugin"))
                score = 45;
            else
                score = underLib ? 70 : 95;
            return true;
        }

        if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            kind = "exe";
            score = inBinish ? 95 : 90;
            return true;
        }

        if (ext.Equals(".wasm", StringComparison.OrdinalIgnoreCase))
        {
            kind = "wasm";
            score = 80;
            return true;
        }

        if (inBinish)
        {
            if (ext.Length == 0 || ext.Equals(".bin", StringComparison.OrdinalIgnoreCase))
            {
                kind = "bin";
                score = 70;
                return true;
            }

            if (ext is ".cmd" or ".bat" or ".ps1" or ".sh")
            {
                kind = "bin";
                score = 55;
                return true;
            }
        }

        return false;
    }

    static bool IsDependencyJarName(string name)
    {
        var n = name.ToLowerInvariant();
        ReadOnlySpan<string> prefixes =
        [
            "asm-", "asm_", "bcel-", "commons-", "guava", "gson-", "slf4j", "log4j",
            "httpclient", "httpcore", "kotlin-", "scala-", "rhino-", "antlr", "picocli-",
            "jaxen-", "dom4j-", "jcip-", "jsr305", "jsr250", "error_prone", "checker-",
            "j2objc", "listenablefuture", "failureaccess", "jspecify", "pcollections",
            "xmlresolver", "saxon-", "progressbar", "jline-", "jna-", "jsoup-",
            "flogger", "better-files", "directory-watcher", "geny_", "sourcecode_",
            "ujson_", "upack_", "upickle", "trees_", "parsers_", "io_", "common_",
            "scalajs-", "groovy-", "spotbugs-annotations", "spotbugs-ant"
        ];
        foreach (var p in prefixes)
        {
            if (n.StartsWith(p, StringComparison.Ordinal))
                return true;
        }

        return n.EndsWith("-annotations.jar", StringComparison.Ordinal)
               || n.Contains("annotation", StringComparison.Ordinal);
    }

    static string GuessKind(string absPath)
    {
        var ext = Path.GetExtension(absPath);
        if (ext.Equals(".jar", StringComparison.OrdinalIgnoreCase)) return "jar";
        if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)) return "exe";
        if (ext.Equals(".wasm", StringComparison.OrdinalIgnoreCase)) return "wasm";
        return "bin";
    }

    static void CopyDirectory(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(src))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);

        foreach (var dir in Directory.EnumerateDirectories(src))
        {
            var name = Path.GetFileName(dir);
            if (name is "node_modules" or ".git")
                continue;
            CopyDirectory(dir, Path.Combine(dest, name));
        }
    }

    static string NormalizeGroupId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";
        var s = raw.Trim().ToLowerInvariant();
        Span<char> buf = stackalloc char[s.Length];
        var n = 0;
        var prevDash = false;
        foreach (var ch in s)
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                buf[n++] = ch;
                prevDash = false;
            }
            else if (!prevDash)
            {
                buf[n++] = '-';
                prevDash = true;
            }
        }

        var id = new string(buf[..n]).Trim('-');
        return id.Length > 32 ? id[..32].TrimEnd('-') : id;
    }

    static string PrettyLabel(string id)
    {
        if (id.StartsWith("lang-", StringComparison.OrdinalIgnoreCase))
            return "Lang: " + id[5..];
        if (id is "ungrouped")
            return "Ungrouped";
        if (id.Length == 0)
            return id;
        return char.ToUpperInvariant(id[0]) + id[1..].Replace('-', ' ');
    }

    static string? Prop(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    static string Trunc(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";
}
