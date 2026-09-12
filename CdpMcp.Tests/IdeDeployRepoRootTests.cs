using System.Text.Json;
using Cdp.Core;
using Cdp.Deploy;
using Xunit;

namespace CdpMcp.Tests;

public sealed class IdeDeployRepoRootTests
{
    [Fact]
    public void ResolveRepoSearchRoot_prefers_arg_over_session()
    {
        var source = CdpDeploySource.TryResolve(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..")));
        Assert.NotNull(source);

        var session = new SessionContext { ProjectRoot = @"D:\somewhere-else" };
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["repo_search_root"] = JsonSerializer.SerializeToElement(source!.RepoRoot)
        };

        var resolved = IdeDeploy.ResolveRepoSearchRoot(session, args);
        Assert.Equal(source.RepoRoot, resolved, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveConfiguredRepoRoot_reads_deploy_section_from_seat_toml()
    {
        var source = CdpDeploySource.TryResolve(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..")));
        Assert.NotNull(source);

        var seat = Path.Combine(Path.GetTempPath(), "cdp-deploy-repo-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(seat);
        try
        {
            File.WriteAllText(
                Path.Combine(seat, "cdp-mcp.toml"),
                $"""
                version = 1
                [deploy]
                repo_root = "{source!.RepoRoot.Replace("\\", "/")}"
                """);

            var resolved = IdeDeploy.ResolveConfiguredRepoRoot(seat);
            Assert.Equal(source.RepoRoot, resolved, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(seat, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Rollout_dry_run_finds_source_from_repo_search_root_without_session()
    {
        var source = CdpDeploySource.TryResolve(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..")));
        Assert.NotNull(source);

        var json = IdeDeploy.Run(
            new SessionContext(),
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["mode"] = JsonSerializer.SerializeToElement("rollout"),
                ["dry_run"] = JsonSerializer.SerializeToElement(true),
                ["repo_search_root"] = JsonSerializer.SerializeToElement(source!.RepoRoot)
            });

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean(), json);
        Assert.Equal("rollout", doc.RootElement.GetProperty("op").GetString());
        var steps = doc.RootElement.GetProperty("steps");
        Assert.Equal(3, steps.GetArrayLength());
        var labels = steps.EnumerateArray().Select(s => s.GetProperty("label").GetString()).ToList();
        Assert.Equal(["soft_sibling", "soft_self", "apply_staged"], labels);
        Assert.DoesNotContain(steps.EnumerateArray(), s =>
            string.Equals(s.GetProperty("mode").GetString(), "hard", StringComparison.Ordinal));
    }
}
