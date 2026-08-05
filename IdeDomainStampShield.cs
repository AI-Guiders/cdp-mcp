#nullable enable
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

/// <summary>
/// Domain stamp shield for #CIDE ships — Done without fresh <c>last_ship</c> is rooster-wait.
/// Parity with <see cref="IdeHumanFaceShield"/>: refuse until <c>domain=</c> card has fresh stamp or force=.
/// Stamp moment = same turn as ship; L1 = check already stamped, not first write.
/// </summary>
internal static class IdeDomainStampShield
{
    internal const string RefuseId = "domain_stamp_missing";

    /// <summary>When leaf wall start unknown — card mtime / last_ship date within this window counts as fresh.</summary>
    internal static TimeSpan FreshWindow { get; set; } = TimeSpan.FromHours(12);

    static readonly Regex IsoDate = new(
        @"\b(?<d>\d{4}-\d{2}-\d{2})\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static void RefuseCideDoneWithoutFreshStamp(
        IntentWorkspaceStore store,
        Guid stageId,
        IReadOnlyDictionary<string, JsonElement>? args,
        string? projectRoot = null)
    {
        if (ForceArg(args))
            return;

        var peek = store.TryGetStageTitleProduct(stageId);
        if (peek is null)
            return;
        var (_, product) = peek.Value;
        if (!string.Equals(product, "CIDE", StringComparison.OrdinalIgnoreCase))
            return;

        var domainId = DomainArg(args);
        if (string.IsNullOrWhiteSpace(domainId))
        {
            throw new ArgumentException(
                $"task_done refused — {RefuseId}: #CIDE ship needs domain=<card-id> with fresh ## last_ship " +
                "(stamp same turn as ship; L1 ≠ stamp moment). force=true escape.");
        }

        var root = projectRoot
                   ?? Opt(args, "project_root")
                   ?? Opt(args, "workspace_path")
                   ?? IdePressureChannel.TryPeekProjectRoot();

        if (!HasFreshStamp(root, domainId.Trim(), leafNotBefore: null, out var detail))
        {
            throw new ArgumentException(
                $"task_done refused — {RefuseId}: domain={domainId.Trim()} — {detail}. " +
                "Stamp ## last_ship on .cdp/domain/<id>.md this turn (not at L1). force=true escape.");
        }

        IdeDomainStampPending.Clear();
    }

    /// <summary>
    /// Fresh when card file mtime or any ## last_ship ISO date is on/after <paramref name="leafNotBefore"/>,
    /// or (when leaf unknown) within <see cref="FreshWindow"/> / today's calendar date.
    /// </summary>
    internal static bool HasFreshStamp(
        string? projectRoot,
        string domainId,
        DateTimeOffset? leafNotBefore,
        out string detail)
    {
        detail = "no card";
        if (string.IsNullOrWhiteSpace(domainId))
        {
            detail = "empty domain id";
            return false;
        }

        var path = ResolveCardPath(projectRoot, domainId);
        if (path is null || !File.Exists(path))
        {
            detail = $"card file missing under .cdp/domain ({domainId}.md)";
            return false;
        }

        DateTimeOffset mtime;
        try
        {
            mtime = File.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex)
        {
            detail = "mtime unreadable: " + ex.Message;
            return false;
        }

        var floor = leafNotBefore ?? DateTimeOffset.UtcNow - FreshWindow;
        if (mtime >= floor)
        {
            detail = $"mtime utc {mtime:yyyy-MM-dd HH:mm} ≥ floor {floor:yyyy-MM-dd HH:mm}";
            return true;
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            detail = "read failed: " + ex.Message;
            return false;
        }

        var lastShip = ExtractLastShipSection(text);
        if (lastShip.Length == 0)
        {
            detail = "no ## last_ship section (or empty) and mtime stale";
            return false;
        }

        DateTimeOffset? newest = null;
        foreach (Match m in IsoDate.Matches(lastShip))
        {
            if (!DateTime.TryParseExact(
                    m.Groups["d"].Value,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var day))
                continue;
            var end = new DateTimeOffset(day.Year, day.Month, day.Day, 23, 59, 59, TimeSpan.Zero);
            if (newest is null || end > newest)
                newest = end;
        }

        if (newest is { } n && n >= floor.Date)
        {
            detail = $"last_ship date {n:yyyy-MM-dd} ≥ floor {floor:yyyy-MM-dd}";
            return true;
        }

        // Same calendar day (local) as "stamped today" when leaf floor is older than midnight.
        var todayLocal = DateTime.Now.Date;
        if (newest is { } nd && nd.Date == todayLocal.ToUniversalTime().Date
            || newest is { } nl && nl.UtcDateTime.Date == DateTime.UtcNow.Date)
        {
            detail = "last_ship has today's date";
            return true;
        }

        detail = newest is { } old
            ? $"last_ship newest {old:yyyy-MM-dd} and mtime {mtime:yyyy-MM-dd HH:mm} both before floor {floor:yyyy-MM-dd HH:mm}"
            : $"last_ship has no ISO dates and mtime {mtime:yyyy-MM-dd HH:mm} before floor {floor:yyyy-MM-dd HH:mm}";
        return false;
    }

    internal static string? ResolveCardPath(string? projectRoot, string domainId)
    {
        var dir = IdeDomainPulse.ResolveDir(projectRoot);
        if (dir is null)
            return null;
        var safe = domainId.Trim();
        if (safe.Length == 0 || safe.Contains('/') || safe.Contains('\\') || safe.Contains(".."))
            return null;
        var path = Path.Combine(dir, safe + ".md");
        if (File.Exists(path))
            return path;
        // allow glass-intercom style ids already including extension-less name
        foreach (var f in Directory.EnumerateFiles(dir, "*.md", SearchOption.TopDirectoryOnly))
        {
            if (Path.GetFileNameWithoutExtension(f).Equals(safe, StringComparison.OrdinalIgnoreCase))
                return f;
        }

        return path;
    }

    static string ExtractLastShipSection(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var inSection = false;
        var sb = new System.Text.StringBuilder();
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                var name = line[3..].Trim();
                if (inSection)
                    break;
                inSection = name.Equals("last_ship", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inSection)
                continue;
            sb.AppendLine(line);
        }

        return sb.ToString();
    }

    static string? DomainArg(IReadOnlyDictionary<string, JsonElement>? args)
    {
        if (args is null)
            return null;
        foreach (var key in new[] { "domain", "stamp", "card", "domain_id" })
        {
            var v = Opt(args, key);
            if (v is { Length: > 0 })
                return v;
        }

        if (args.TryGetValue("go_args", out var ga) && ga.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "domain", "stamp", "card", "domain_id" })
            {
                if (ga.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String
                    && el.GetString() is { Length: > 0 } s)
                    return s;
            }
        }

        return null;
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind == JsonValueKind.String ? el.GetString() : null;
    }

    static bool Boolish(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return false;
        if (el.ValueKind == JsonValueKind.True)
            return true;
        return el.ValueKind == JsonValueKind.String
               && bool.TryParse(el.GetString(), out var b)
               && b;
    }

    static bool ForceArg(IReadOnlyDictionary<string, JsonElement>? args)
    {
        if (args is null)
            return false;
        if (Boolish(args, "force"))
            return true;
        if (args.TryGetValue("go_args", out var ga) && ga.ValueKind == JsonValueKind.Object
            && ga.TryGetProperty("force", out var f))
        {
            if (f.ValueKind == JsonValueKind.True)
                return true;
            if (f.ValueKind == JsonValueKind.String && bool.TryParse(f.GetString(), out var b) && b)
                return true;
        }

        return false;
    }
}
