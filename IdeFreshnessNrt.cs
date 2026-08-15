#nullable enable
using System.Text.RegularExpressions;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Status-* Next review / Revisit peel + fire heuristic.
/// Alias→URL SSOT = <see cref="IdeFreshnessCatalog"/>.
/// </summary>
internal static class IdeFreshnessNrt
{
    public static object? HintForUrl(string url, bool changed, string? title)
    {
        if (!IdeFreshnessCatalog.TryResolve(url, out var meta) || meta.Domain is null)
            return null;

        var peeled = Peel(meta, session: null);
        var fire = changed;
        var reason = changed
            ? "fingerprint_changed — dig status NRT before claiming Проверено"
            : "unchanged — NRT for context only";
        if (changed && !string.IsNullOrWhiteSpace(title) && peeled.Triggers.Count > 0)
        {
            var hit = peeled.Triggers.Any(t =>
                title!.Split([' ', '.', '-', '/'], StringSplitOptions.RemoveEmptyEntries)
                    .Any(tok => tok.Length >= 2 && t.Contains(tok, StringComparison.OrdinalIgnoreCase)));
            if (hit) reason = "fingerprint_changed + title token overlaps NRT bullet";
        }

        return new
        {
            fire_suggested = fire,
            reason,
            domain = meta.Domain,
            status = peeled.StatusPath,
            triggers = peeled.Triggers.Take(12).ToArray(),
            note = "Digest ≠ Проверено. Agent digs then stamps via memory_world_*."
        };
    }

    public static object ExplainNrt(SessionContext session, string aliasOrDomainOrUrl)
    {
        IdeFreshnessCatalog.Entry? meta = null;
        string? domain = null;
        if (IdeFreshnessCatalog.TryResolve(aliasOrDomainOrUrl, out var byAlias))
            meta = byAlias;
        else
            domain = aliasOrDomainOrUrl.Trim();

        if (meta is null && !string.IsNullOrWhiteSpace(domain))
        {
            domain = domain!.Replace('\\', '/').Trim('/');
            if (domain.StartsWith("worlds/", StringComparison.OrdinalIgnoreCase))
                domain = domain["worlds/".Length..];
            var status = FindStatusFile(session, domain);
            if (status is null)
            {
                return new
                {
                    schema = "freshness_nrt/v1",
                    ok = false,
                    error = "status_not_found",
                    domain,
                    hint = "alias=avalonia|php|baseline2026 or domain=software-php-laravel"
                };
            }

            meta = new IdeFreshnessCatalog.Entry("", domain, Path.GetFileName(status));
            var peeledDomain = PeelPath(status);
            return NrtPayload(meta, peeledDomain.StatusPath, peeledDomain.Triggers, fire: false, reason: "manual_nrt_lookup");
        }

        if (meta is null)
        {
            return new
            {
                schema = "freshness_nrt/v1",
                ok = false,
                error = "alias_or_domain_required",
                hint = "alias=avalonia | domain=software-javascript"
            };
        }

        var peeled = Peel(meta, session);
        return NrtPayload(meta, peeled.StatusPath, peeled.Triggers, fire: false, reason: "manual_nrt_lookup");
    }

    static object NrtPayload(
        IdeFreshnessCatalog.Entry meta,
        string? statusPath,
        IReadOnlyList<string> triggers,
        bool fire,
        string reason) => new
    {
        schema = "freshness_nrt/v1",
        ok = statusPath is not null,
        op = "nrt",
        go = IdeFreshnessChannel.GoName,
        tool = IdeFreshnessChannel.ToolName,
        alias_url = string.IsNullOrEmpty(meta.Url) ? null : meta.Url,
        domain = meta.Domain,
        status = statusPath,
        fire_suggested = fire,
        reason,
        triggers = triggers.Take(20).ToArray(),
        hint = "status-* NRT stronger than global TTL. Digest ≠ Проверено."
    };

    sealed record Peeled(string? StatusPath, List<string> Triggers);

    static Peeled Peel(IdeFreshnessCatalog.Entry meta, SessionContext? session)
    {
        if (meta.Domain is null)
            return new Peeled(null, []);
        var path = FindStatusFile(session, meta.Domain, meta.StatusFile);
        return PeelPath(path);
    }

    static Peeled PeelPath(string? path)
    {
        if (path is null || !File.Exists(path))
            return new Peeled(null, []);
        string text;
        try { text = File.ReadAllText(path); }
        catch { return new Peeled(path, []); }
        return new Peeled(path, ExtractTriggers(text));
    }

    internal static List<string> ExtractTriggers(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var triggers = new List<string>();
        var inSection = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            if (Regex.IsMatch(line, @"^##\s+(Next review triggers|Revisit triggers)\b", RegexOptions.IgnoreCase)
                || Regex.IsMatch(line, @"^\*\*Next review triggers:\*\*", RegexOptions.IgnoreCase))
            {
                inSection = true;
                var inline = Regex.Replace(line, @"^##\s+.*?$|^\*\*Next review triggers:\*\*\s*", "", RegexOptions.IgnoreCase).Trim();
                if (inline.Length > 0 && inline.StartsWith('-'))
                    triggers.Add(TrimBullet(inline));
                continue;
            }

            if (inSection)
            {
                if (line.StartsWith("## ", StringComparison.Ordinal))
                    break;
                if (line.TrimStart().StartsWith('-'))
                    triggers.Add(TrimBullet(line));
            }
        }

        if (triggers.Count == 0)
        {
            inSection = false;
            foreach (var raw in lines)
            {
                var line = raw.TrimEnd();
                if (Regex.IsMatch(line, @"^##\s+Maintenance Policy\b", RegexOptions.IgnoreCase))
                {
                    inSection = true;
                    continue;
                }
                if (inSection)
                {
                    if (line.StartsWith("## ", StringComparison.Ordinal)) break;
                    if (line.TrimStart().StartsWith('-')
                        && Regex.IsMatch(line, @"review|trigger|release|edition|URL rot|stale", RegexOptions.IgnoreCase))
                        triggers.Add(TrimBullet(line));
                }
            }
        }

        return triggers;
    }

    static string TrimBullet(string line)
    {
        line = line.Trim();
        if (line.StartsWith("- ")) line = line[2..];
        else if (line.StartsWith('-')) line = line[1..].TrimStart();
        return line.Trim();
    }

    static string? FindStatusFile(SessionContext? session, string domain, string? preferName = null)
    {
        domain = domain.Trim().Replace('\\', '/').Trim('/');
        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(session?.ProjectRoot))
        {
            roots.Add(Path.Combine(session!.ProjectRoot!, "knowledge", "worlds", domain));
            roots.Add(Path.Combine(session.ProjectRoot!, "worlds", domain));
        }

        var canon = Environment.GetEnvironmentVariable("AGENT_NOTES_CANON_PATH");
        if (!string.IsNullOrWhiteSpace(canon))
            roots.Add(Path.Combine(canon, "knowledge", "worlds", domain));

        roots.Add(Path.Combine(@"D:\Experiments\agent-notes", "knowledge", "worlds", domain));

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root)) continue;
            if (!string.IsNullOrWhiteSpace(preferName))
            {
                var preferred = Path.Combine(root, preferName!);
                if (File.Exists(preferred)) return preferred;
            }

            var hit = Directory.EnumerateFiles(root, "status-*.md", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (hit is not null) return hit;
        }

        return null;
    }
}
