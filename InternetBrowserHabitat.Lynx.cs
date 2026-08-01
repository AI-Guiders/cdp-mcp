using System.Diagnostics;
using System.Text;

namespace CdpMcp;

/// <summary>lynx resolve + process + dump parse (≤ADX soft-warn peel).</summary>
internal sealed partial class InternetBrowserHabitat
{
    static PageFetch FetchPage(string lynxExe, string url, int width, int timeoutSeconds, string userAgent)
    {
        // Chromium UA: many gates sniff Lynx/2.x and dump «update browser». Google still wants JS after that.
        var (code, stdout, stderr, ms) = RunProcessTimed(
            lynxExe,
            ["-dump", $"-width={width}", "-useragent=" + userAgent, url],
            timeoutSeconds);

        var (text, links) = SplitDumpAndLinks(stdout, url);
        return new PageFetch(url, text, links, code, stderr, ms);
    }

    static string ResolveUserAgent() => IdeSettingsHabitat.EffectiveUserAgent();

    static (string Text, IReadOnlyList<LinkRef> Links) SplitDumpAndLinks(string dump, string pageUrl)
    {
        if (string.IsNullOrEmpty(dump))
            return ("", Array.Empty<LinkRef>());

        var lines = dump.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var refIdx = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim().Equals("References", StringComparison.OrdinalIgnoreCase))
            {
                refIdx = i;
                break;
            }
        }

        if (refIdx < 0)
            return (dump.TrimEnd(), Array.Empty<LinkRef>());

        var text = string.Join('\n', lines.AsSpan(0, refIdx)).TrimEnd();
        var links = new List<LinkRef>();
        for (var i = refIdx + 1; i < lines.Length; i++)
        {
            var m = RefLine.Match(lines[i]);
            if (!m.Success) continue;
            if (!int.TryParse(m.Groups[1].Value, out var n)) continue;
            var href = m.Groups[2].Value.Trim();
            if (href.Length == 0) continue;
            href = ResolveHref(pageUrl, href);
            links.Add(new LinkRef(n, href, null));
        }

        return (text, links);
    }

    static string ResolveHref(string pageUrl, string href)
    {
        if (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || href.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || href.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return href;

        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var baseUri))
            return href;
        return Uri.TryCreate(baseUri, href, out var abs) ? abs.ToString() : href;
    }

    static string NormalizeUrl(string url)
    {
        url = url.Trim();
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return url;
        return "https://" + url;
    }

    internal static LynxResolve ResolveLynx()
    {
        foreach (var key in new[] { "CDP_LYNX", "CDP_BROWSER_LYNX", "LYNX" })
        {
            var env = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(env) && File.Exists(env.Trim()))
                return LynxResolve.Found(Path.GetFullPath(env.Trim()));
        }

        var fromPath = FindOnPath("lynx.exe") ?? FindOnPath("lynx");
        if (fromPath is not null)
            return LynxResolve.Found(fromPath);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var candidate in new[]
                 {
                     Path.Combine(home, "scoop", "shims", "lynx.exe"),
                     Path.Combine(home, "scoop", "apps", "lynx", "current", "lynx.exe"),
                     Path.Combine(home, "scoop", "apps", "lynx", "current", "bin", "lynx.exe")
                 })
        {
            if (File.Exists(candidate))
                return LynxResolve.Found(candidate);
        }

        return LynxResolve.Missing(
            "lynx.exe not found on PATH / scoop shims. Install: scoop install lynx. Or set CDP_LYNX.");
    }

    static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var full = Path.Combine(dir.Trim().Trim('"'), fileName);
                if (File.Exists(full))
                    return Path.GetFullPath(full);
            }
            catch
            {
                /* skip bad PATH entries */
            }
        }

        return null;
    }

    static (int ExitCode, string Stdout, string Stderr) RunProcess(
        string exe, IReadOnlyList<string> argv, int timeoutSeconds)
    {
        var (code, stdout, stderr, _) = RunProcessTimed(exe, argv, timeoutSeconds);
        return (code, stdout, stderr);
    }

    static (int ExitCode, string Stdout, string Stderr, int ElapsedMs) RunProcessTimed(
        string exe, IReadOnlyList<string> argv, int timeoutSeconds)
    {
        var sw = Stopwatch.StartNew();
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var a in argv)
            psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start " + exe);

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        if (!proc.WaitForExit(timeoutSeconds * 1000))
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw new TimeoutException($"lynx timed out after {timeoutSeconds}s");
        }

        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        sw.Stop();
        return (proc.ExitCode, stdout, stderr, (int)sw.ElapsedMilliseconds);
    }

    internal readonly record struct LynxResolve(bool Ok, string? Path, string? Error)
    {
        public static LynxResolve Found(string path) => new(true, path, null);
        public static LynxResolve Missing(string error) => new(false, null, error);
    }
}
