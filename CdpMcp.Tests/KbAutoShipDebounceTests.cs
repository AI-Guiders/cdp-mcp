using System.Diagnostics;
using Xunit;

namespace CdpMcp.Tests;

public sealed class KbAutoShipDebounceTests : IDisposable
{
    readonly string _repo;
    readonly List<string> _gitLog = [];
    readonly List<string> _alerts = [];

    public KbAutoShipDebounceTests()
    {
        _repo = Directory.CreateTempSubdirectory("kb-autoship-").FullName;
        InitBareRepo();
        KbAutoShipGitRunner.ExecOverride = GitStub;
        KbAutoShipEngine.AlertOverride = a => _alerts.Add(a);
    }

    public void Dispose()
    {
        KbAutoShipGitRunner.ExecOverride = null;
        KbAutoShipEngine.AlertOverride = null;
        try { Directory.Delete(_repo, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task Burst_writes_coalesce_to_one_ship()
    {
        var options = new KbAutoShipOptions
        {
            Enabled = true,
            DebounceMs = 80,
            Roots = [_repo],
            Branch = "main"
        };
        using var svc = new KbAutoShipService(options);
        svc.Start();

        File.WriteAllText(Path.Combine(_repo, "a.md"), "one");
        svc.NotifyChange(_repo);
        await Task.Delay(20);
        File.WriteAllText(Path.Combine(_repo, "b.md"), "two");
        svc.NotifyChange(_repo);
        await Task.Delay(20);
        File.WriteAllText(Path.Combine(_repo, "c.md"), "three");
        svc.NotifyChange(_repo);

        await Task.Delay(250);

        Assert.Equal(1, _gitLog.Count(l => l.Contains("commit -m", StringComparison.Ordinal)));
    }

    [Fact]
    public void Origin_ahead_alerts_and_skips_commit_and_push()
    {
        var engine = new KbAutoShipEngine(a => _alerts.Add(a));
        var options = new KbAutoShipOptions { Branch = "main", Roots = [_repo] };

        KbAutoShipGitRunner.ExecOverride = (cwd, args) =>
        {
            if (args.StartsWith("rev-list HEAD..origin/main", StringComparison.Ordinal))
                return new KbGitExecResult(0, "2", "");
            if (args.StartsWith("fetch", StringComparison.Ordinal))
                return new KbGitExecResult(0, "", "");
            if (args.StartsWith("status --porcelain", StringComparison.Ordinal))
                return new KbGitExecResult(0, " M knowledge/foo.md", "");
            _gitLog.Add(args);
            return new KbGitExecResult(0, "", "");
        };

        var result = engine.TryShip(_repo, options);

        Assert.Equal(KbAutoShipOutcome.Failed, result.Outcome);
        Assert.Contains(_alerts, a => a.Contains("ahead", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(_gitLog, l => l.Contains("commit", StringComparison.Ordinal));
        Assert.DoesNotContain(_gitLog, l => l.Contains("push", StringComparison.Ordinal));
    }

    [Fact]
    public void Push_rejected_alerts_operator()
    {
        var engine = new KbAutoShipEngine(a => _alerts.Add(a));
        var options = new KbAutoShipOptions { Branch = "main", Roots = [_repo] };

        KbAutoShipGitRunner.ExecOverride = (cwd, args) =>
        {
            if (args.StartsWith("rev-list", StringComparison.Ordinal))
                return new KbGitExecResult(0, "0", "");
            if (args.StartsWith("fetch", StringComparison.Ordinal))
                return new KbGitExecResult(0, "", "");
            if (args.StartsWith("status --porcelain", StringComparison.Ordinal))
                return new KbGitExecResult(0, " M note.md", "");
            if (args.StartsWith("add", StringComparison.Ordinal) || args.StartsWith("commit", StringComparison.Ordinal))
                return new KbGitExecResult(0, "", "");
            if (args.StartsWith("push", StringComparison.Ordinal))
                return new KbGitExecResult(1, "", "rejected");
            return new KbGitExecResult(0, "", "");
        };

        var result = engine.TryShip(_repo, options);

        Assert.Equal(KbAutoShipOutcome.Failed, result.Outcome);
        Assert.Contains(_alerts, a => a.Contains("push rejected", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildCommitMessage_summarizes_top_paths()
    {
        const string porcelain = """
            M knowledge/a.md
            M knowledge/b.md
            M knowledge/c.md
            M knowledge/d.md
            """;

        var msg = KbAutoShipEngine.BuildCommitMessage(porcelain);
        Assert.StartsWith("KB auto:", msg, StringComparison.Ordinal);
        Assert.Contains("a.md", msg, StringComparison.Ordinal);
        Assert.Contains("+1 more", msg, StringComparison.Ordinal);
    }

    void InitBareRepo()
    {
        RunGit(_repo, "init -b main");
        File.WriteAllText(Path.Combine(_repo, "README.md"), "seed");
        RunGit(_repo, "add -A");
        RunGit(_repo, "commit -m \"seed\"");
    }

    KbGitExecResult GitStub(string cwd, string args)
    {
        _gitLog.Add(args);
        if (args.StartsWith("rev-list HEAD..origin/main", StringComparison.Ordinal))
            return new KbGitExecResult(0, "0", "");
        if (args.StartsWith("fetch", StringComparison.Ordinal))
            return new KbGitExecResult(0, "", "");
        if (args.StartsWith("status --porcelain", StringComparison.Ordinal))
        {
            var dirty = Directory.GetFiles(_repo, "*", SearchOption.AllDirectories)
                .Any(f => !f.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && File.GetLastWriteTimeUtc(f) > DateTime.UtcNow.AddMinutes(-1));
            return dirty
                ? new KbGitExecResult(0, "?? new.md", "")
                : new KbGitExecResult(0, "", "");
        }

        if (args.StartsWith("add", StringComparison.Ordinal) || args.StartsWith("commit", StringComparison.Ordinal) || args.StartsWith("push", StringComparison.Ordinal))
            return new KbGitExecResult(0, "", "");

        return new KbGitExecResult(0, "", "");
    }

    static void RunGit(string cwd, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"git {args} failed: {p.StandardError.ReadToEnd()}");
    }
}
