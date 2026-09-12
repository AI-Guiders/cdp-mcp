namespace CdpMcp;

internal enum KbAutoShipOutcome
{
    Clean,
    Shipped,
    Failed
}

internal sealed record KbAutoShipResult(KbAutoShipOutcome Outcome, string? Detail = null, string? CommitMessage = null);

/// <summary>CDP-ADR-0224: debounced ship logic — fetch, conflict gate, commit, push.</summary>
internal sealed class KbAutoShipEngine
{
    readonly KbAutoShipGitRunner _git = new();
    readonly Action<string> _alert;

    internal static Action<string>? AlertOverride { get; set; }

    public KbAutoShipEngine(Action<string>? alert = null)
    {
        _alert = alert ?? DefaultAlert;
    }

    public KbAutoShipResult TryShip(string repoRoot, KbAutoShipOptions options)
    {
        if (!Directory.Exists(repoRoot) || !Directory.Exists(Path.Combine(repoRoot, ".git")))
            return new KbAutoShipResult(KbAutoShipOutcome.Failed, $"not a git repo: {repoRoot}");

        var branch = options.Branch;
        var remoteRef = $"origin/{branch}";

        var fetch = _git.Run(repoRoot, "fetch origin");
        if (fetch.ExitCode != 0)
            return AlertFail(repoRoot, $"fetch failed: {Trim(fetch.Stderr)}");

        var behind = _git.Run(repoRoot, $"rev-list HEAD..{remoteRef} --count");
        if (behind.ExitCode == 0
            && int.TryParse(behind.Stdout.Trim(), out var ahead)
            && ahead > 0)
        {
            return AlertFail(
                repoRoot,
                $"{remoteRef} is {ahead} commit(s) ahead of local HEAD — operator must reconcile (no auto-rebase).",
                conflict: true);
        }

        var secretHits = IdeReviewChannel.ListDirtyFiles(repoRoot)
            .Count(d => d.Risk.Equals("secret", StringComparison.OrdinalIgnoreCase));
        if (secretHits > 0)
            return AlertFail(repoRoot, $"blocked: {secretHits} secret-risk path(s) in working tree.");

        var add = _git.Run(repoRoot, "add -A");
        if (add.ExitCode != 0)
            return AlertFail(repoRoot, $"git add failed: {Trim(add.Stderr)}");

        var status = _git.Run(repoRoot, "status --porcelain");
        if (status.ExitCode != 0)
            return AlertFail(repoRoot, $"git status failed: {Trim(status.Stderr)}");
        if (string.IsNullOrWhiteSpace(status.Stdout))
            return new KbAutoShipResult(KbAutoShipOutcome.Clean);

        var message = BuildCommitMessage(status.Stdout);
        var commit = _git.Run(repoRoot, $"commit -m {QuoteArg(message)}");
        if (commit.ExitCode != 0)
            return AlertFail(repoRoot, $"commit failed: {Trim(commit.Stderr)}");

        var push = _git.Run(repoRoot, $"push origin {branch}");
        if (push.ExitCode != 0)
            return AlertFail(repoRoot, $"push rejected: {Trim(push.Stderr)}", conflict: true);

        return new KbAutoShipResult(KbAutoShipOutcome.Shipped, CommitMessage: message);
    }

    KbAutoShipResult AlertFail(string repoRoot, string detail, bool conflict = false)
    {
        var body = $"[{Path.GetFileName(repoRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}] {detail}";
        _alert(body);
        return new KbAutoShipResult(KbAutoShipOutcome.Failed, body);
    }

    static void DefaultAlert(string detail)
    {
        if (AlertOverride is { } ov)
        {
            ov(detail);
            return;
        }

        Console.Error.WriteLine($"[KbAutoShip] {detail}");
        try
        {
            _ = CideWakeDispatch.Enqueue(
                CideWakeDispatch.KindLetter,
                $"KB AutoShip: {detail}",
                nick: "Оператор",
                from: "KbAutoShip",
                task: "kb_autoship");
        }
        catch
        {
            /* best effort — stderr already logged */
        }
    }

    internal static string BuildCommitMessage(string porcelain)
    {
        var paths = ParsePorcelainPaths(porcelain);
        var summary = BuildPathSummary(paths);
        return $"KB auto: {summary}";
    }

    internal static IReadOnlyList<string> ParsePorcelainPaths(string porcelain)
    {
        var paths = new List<string>();
        foreach (var raw in porcelain.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (raw.Length < 4)
                continue;
            var path = raw[3..].Trim();
            if (path.Contains(" -> ", StringComparison.Ordinal))
                path = path[(path.LastIndexOf(" -> ", StringComparison.Ordinal) + 4)..];
            if (path.Length > 0)
                paths.Add(path);
        }

        return paths;
    }

    static string BuildPathSummary(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            return "(changes)";
        if (paths.Count <= 3)
            return string.Join(", ", paths);
        return string.Join(", ", paths.Take(3)) + $" +{paths.Count - 3} more";
    }

    static string QuoteArg(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    static string Trim(string? s) => string.IsNullOrWhiteSpace(s) ? "(no stderr)" : s.Trim();
}
