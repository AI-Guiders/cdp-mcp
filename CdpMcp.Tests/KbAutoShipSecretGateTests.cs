using Xunit;

namespace CdpMcp.Tests;

public sealed class KbAutoShipSecretGateTests : IDisposable
{
    readonly string _repo;
    readonly List<string> _gitCalls = [];
    readonly List<string> _alerts = [];

    public KbAutoShipSecretGateTests()
    {
        _repo = Directory.CreateTempSubdirectory("kb-autoship-gate-").FullName;
        Directory.CreateDirectory(Path.Combine(_repo, ".git"));
        KbAutoShipEngine.AlertOverride = a => _alerts.Add(a);
    }

    public void Dispose()
    {
        KbAutoShipGitRunner.ExecOverride = null;
        KbAutoShipSecretScanner.ExecOverride = null;
        KbAutoShipSecretScanner.ResetResolutionCache();
        try { Directory.Delete(_repo, recursive: true); } catch { /* best effort */ }
    }

    KbAutoShipOptions Options(string scanner = "auto") => new()
    {
        Branch = "main",
        Roots = [_repo],
        SecretScanner = scanner
    };

    void StubGit(bool dirty)
    {
        KbAutoShipGitRunner.ExecOverride = (cwd, args) =>
        {
            _gitCalls.Add(args);
            if (args.StartsWith("status --porcelain", StringComparison.Ordinal))
                return new KbGitExecResult(0, dirty ? " M note.md\n" : "", "");
            if (args.StartsWith("rev-list ", StringComparison.Ordinal))
                return new KbGitExecResult(0, "0", "");
            return new KbGitExecResult(0, "", "");
        };
    }

    [Fact]
    public void Gitleaks_findings_block_the_ship()
    {
        StubGit(dirty: true);
        KbAutoShipSecretScanner.ExecOverride = (exe, args) =>
            args.StartsWith("detect", StringComparison.Ordinal)
                ? new KbGitExecResult(1, "Finding: generic-api-key ...", "")
                : new KbGitExecResult(0, "", "");

        var result = new KbAutoShipEngine(a => _alerts.Add(a)).TryShip(_repo, Options());

        Assert.Equal(KbAutoShipOutcome.Failed, result.Outcome);
        Assert.Contains(_alerts, a => a.Contains("gitleaks", StringComparison.Ordinal));
        Assert.DoesNotContain(_gitCalls, c => c.StartsWith("add", StringComparison.Ordinal));
    }

    [Fact]
    public void Clean_scan_ships()
    {
        StubGit(dirty: true);
        KbAutoShipSecretScanner.ExecOverride = (_, args) => new KbGitExecResult(0, "", "");

        var result = new KbAutoShipEngine(a => _alerts.Add(a)).TryShip(_repo, Options());

        Assert.Equal(KbAutoShipOutcome.Clean, result.Outcome);
        Assert.Contains(_gitCalls, c => c.StartsWith("add -A", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_scanner_fails_open_and_alerts()
    {
        StubGit(dirty: true);

        var result = new KbAutoShipEngine(a => _alerts.Add(a))
            .TryShip(_repo, Options(scanner: "C:/definitely-missing-gitleaks.exe"));

        Assert.Equal(KbAutoShipOutcome.Clean, result.Outcome);
        Assert.Contains(_alerts, a => a.Contains("secret gate skipped", StringComparison.Ordinal));
        Assert.Contains(_gitCalls, c => c.StartsWith("add -A", StringComparison.Ordinal));
    }

    [Fact]
    public void Off_disables_the_gate()
    {
        StubGit(dirty: true);
        KbAutoShipSecretScanner.ExecOverride = (_, args) => new KbGitExecResult(1, "Finding", "");

        var result = new KbAutoShipEngine(a => _alerts.Add(a)).TryShip(_repo, Options(scanner: "off"));

        Assert.Equal(KbAutoShipOutcome.Clean, result.Outcome);
        Assert.DoesNotContain(_alerts, a => a.Contains("gitleaks", StringComparison.Ordinal));
    }
}
