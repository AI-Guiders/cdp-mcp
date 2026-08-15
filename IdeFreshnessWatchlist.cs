#nullable enable
using System.Text.Json;
using System.Text.RegularExpressions;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Resolve watch URLs from alias=/urls=/domain=/path=.</summary>
internal static class IdeFreshnessWatchlist
{
    static readonly Regex HttpsUrl = new(
        @"https?://[^\s\)\]""'<>]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static List<string> Resolve(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        out string source)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var a in IdeFreshnessArgs.SplitCsv(IdeFreshnessArgs.Opt(args, "alias") ?? IdeFreshnessArgs.Opt(args, "aliases")))
        {
            if (IdeFreshnessCatalog.TryMapUrl(a, out var u))
                set.Add(u);
            else if (a.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                set.Add(a);
        }

        foreach (var u in IdeFreshnessArgs.SplitCsv(IdeFreshnessArgs.Opt(args, "urls") ?? IdeFreshnessArgs.Opt(args, "url")))
        {
            if (IdeFreshnessCatalog.TryMapUrl(u, out var mapped))
                set.Add(mapped);
            else if (u.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                set.Add(TrimUrl(u));
        }

        var domain = IdeFreshnessArgs.Opt(args, "domain") ?? IdeFreshnessArgs.Opt(args, "world");
        if (!string.IsNullOrWhiteSpace(domain))
        {
            foreach (var u in ExtractUrlsFromDomain(session, domain!))
                set.Add(u);
        }

        var path = IdeFreshnessArgs.Opt(args, "path") ?? IdeFreshnessArgs.Opt(args, "file");
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            foreach (Match m in HttpsUrl.Matches(File.ReadAllText(path)))
                set.Add(TrimUrl(m.Value));
        }

        if (set.Count == 0)
        {
            source = "empty";
            return [];
        }

        source = domain is not null ? $"domain:{domain}"
            : path is not null ? $"file:{path}"
            : IdeFreshnessArgs.Opt(args, "alias") is not null ? "alias"
            : "urls";
        return set.OrderBy(u => u, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static IEnumerable<string> ExtractUrlsFromDomain(SessionContext session, string domain)
    {
        domain = domain.Trim().Replace('\\', '/').Trim('/');
        if (domain.StartsWith("worlds/", StringComparison.OrdinalIgnoreCase))
            domain = domain["worlds/".Length..];

        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(session.ProjectRoot))
        {
            roots.Add(Path.Combine(session.ProjectRoot, "knowledge", "worlds", domain));
            roots.Add(Path.Combine(session.ProjectRoot, "worlds", domain));
        }

        var canon = Environment.GetEnvironmentVariable("AGENT_NOTES_CANON_PATH");
        if (!string.IsNullOrWhiteSpace(canon))
            roots.Add(Path.Combine(canon, "knowledge", "worlds", domain));

        roots.Add(Path.Combine(@"D:\Experiments\agent-notes", "knowledge", "worlds", domain));

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root))
                continue;
            foreach (var file in Directory.EnumerateFiles(root, "*.md", SearchOption.TopDirectoryOnly).Take(80))
            {
                string text;
                try { text = File.ReadAllText(file); }
                catch { continue; }
                foreach (Match m in HttpsUrl.Matches(text))
                    yield return TrimUrl(m.Value);
            }
            yield break;
        }
    }

    static string TrimUrl(string u)
    {
        u = u.Trim().TrimEnd('.', ',', ';', ')', ']');
        return u;
    }
}
