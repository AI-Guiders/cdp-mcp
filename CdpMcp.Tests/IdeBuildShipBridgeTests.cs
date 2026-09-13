using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public sealed class IdeBuildShipBridgeTests
{
    [Fact]
    public void AnnotateBuildResult_adds_next_when_green_and_dirty()
    {
        var dir = CreateTempGitRepo();
        try
        {
            File.WriteAllText(Path.Combine(dir, "dirty.txt"), "x");
            var session = new SessionContext { ProjectRoot = dir, ScmRoot = dir };
            var buildJson = """{"success":true,"pulse":"build ok E×0 W×0","exit_code":0}""";

            var annotated = IdeBuildShipBridge.AnnotateBuildResult(buildJson, session);

            using var doc = JsonDocument.Parse(annotated);
            Assert.True(doc.RootElement.TryGetProperty("next", out var next));
            Assert.Equal(JsonValueKind.Array, next.ValueKind);
            Assert.True(next.GetArrayLength() > 0);
            Assert.Equal("git_draft", next[0].GetProperty("go").GetString());
            Assert.True(doc.RootElement.TryGetProperty("bridge", out var bridge));
            Assert.Equal("build_ship_bridge/v0", bridge.GetProperty("schema").GetString());
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public void AnnotateBuildResult_skips_when_build_failed()
    {
        var dir = CreateTempGitRepo();
        try
        {
            File.WriteAllText(Path.Combine(dir, "dirty.txt"), "x");
            var session = new SessionContext { ProjectRoot = dir, ScmRoot = dir };
            var buildJson = """{"success":false,"exit_code":1,"error_count":2}""";

            var annotated = IdeBuildShipBridge.AnnotateBuildResult(buildJson, session);

            Assert.Equal(buildJson, annotated);
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public void AnnotateCsxRunReport_adds_next_when_verify_build_green_and_dirty()
    {
        var dir = CreateTempGitRepo();
        try
        {
            File.WriteAllText(Path.Combine(dir, "dirty.txt"), "x");
            var session = new SessionContext { ProjectRoot = dir, ScmRoot = dir };
            var reportJson = """
                {
                  "Ok": true,
                  "Mode": "run",
                  "Steps": [
                    {
                      "Domain": "build",
                      "Underlying": "build_structured",
                      "Result": "{\"success\":true,\"exit_code\":0}"
                    }
                  ]
                }
                """;

            var annotated = IdeBuildShipBridge.AnnotateCsxRunReport(reportJson, session);

            using var doc = JsonDocument.Parse(annotated);
            Assert.True(doc.RootElement.TryGetProperty("next", out var next));
            Assert.True(next.GetArrayLength() > 0);
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    static void TryDeleteDirectory(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
    }

    static string CreateTempGitRepo()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-ship-bridge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        RunGit(dir, "init");
        RunGit(dir, "config user.email test@test.local");
        RunGit(dir, "config user.name test");
        File.WriteAllText(Path.Combine(dir, "seed.txt"), "seed");
        RunGit(dir, "add seed.txt");
        RunGit(dir, "commit -m seed");
        return dir;
    }

    static void RunGit(string dir, string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.WaitForExit(30_000);
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"git {args} failed");
    }
}
