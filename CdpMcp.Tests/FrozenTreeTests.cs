using System.Diagnostics;
using Cdp.ScriptableIde;
using Xunit;

namespace CdpMcp.Tests;

/// <summary>FTC (Freeze Tree Composition, Света 2026-09-07): frozen tree primitive —
/// worktree at HEAD + WIP overlay, consumer owns it, Discard releases.
/// Test formula: sketch verified now; second look later under green tests.</summary>
public class FrozenTreeTests : IDisposable
{
    readonly string _repo;

    public FrozenTreeTests()
    {
        _repo = Path.Combine(Path.GetTempPath(), "cdp-ftc-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_repo);
        Git("init", "-q");
        Git("config", "user.email", "cdp@test");
        Git("config", "user.name", "cdp-test");
        File.WriteAllText(Path.Combine(_repo, "seed.txt"), "seed\n");
        Git("add", "-A");
        Git("commit", "-q", "-m", "seed");
    }

    public void Dispose()
    {
        try { Git("worktree", "prune"); } catch { /* best effort */ }
        try { Directory.Delete(_repo, recursive: true); } catch { /* best effort */ }
    }

    void Git(params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            Arguments = string.Join(' ', args.Select(Quote)),
            WorkingDirectory = _repo,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi)!;
        p.WaitForExit(30_000);
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {p.StandardError.ReadToEnd()}");
    }

    static string Quote(string s) =>
        s.Contains(' ') || s.Contains('\\') ? $"\"{s}\"" : s;

    [Fact]
    public void Prepare_Freeze_Overlays_Wip_And_Registers_Plan()
    {
        var dirty = Path.Combine(_repo, "seed.txt");
        File.WriteAllText(dirty, "wip-change\n");

        var tree = WorktreePlanRunner.PrepareFrozenTree(_repo);

        Assert.True(tree.Ok, tree.Error);
        Assert.NotEmpty(tree.BaseTreeSha);
        Assert.True(Directory.Exists(tree.WorkRoot));
        Assert.Equal("wip-change\n", File.ReadAllText(Path.Combine(tree.WorkRoot, "seed.txt")));
        Assert.Contains(tree.PlanId, WorktreePlanRunner.ListPlanIds());
    }

    [Fact]
    public void Discard_Releases_The_Tree()
    {
        File.WriteAllText(Path.Combine(_repo, "seed.txt"), "wip\n");
        var tree = WorktreePlanRunner.PrepareFrozenTree(_repo);
        Assert.True(tree.Ok, tree.Error);

        var report = WorktreePlanRunner.Discard(tree.PlanId);

        Assert.True(report.Ok, report.Error);
        Assert.False(Directory.Exists(tree.WorkRoot));
        Assert.DoesNotContain(tree.PlanId, WorktreePlanRunner.ListPlanIds());
    }

    [Fact]
    public void Prepare_Refuses_NonGit_Path()
    {
        var plain = Path.Combine(Path.GetTempPath(), "cdp-ftc-nogit-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(plain);
        try
        {
            var tree = WorktreePlanRunner.PrepareFrozenTree(plain);
            Assert.False(tree.Ok);
            Assert.NotEmpty(tree.Error);
        }
        finally
        {
            try { Directory.Delete(plain, recursive: true); } catch { /* best effort */ }
        }
    }
}
