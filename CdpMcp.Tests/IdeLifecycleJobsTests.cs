using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public sealed class IdeLifecycleJobsTests
{
    [Fact]
    public void ResolveBackground_defaults_true()
    {
        Assert.True(IdeLifecycleJobs.ResolveBackground(new Dictionary<string, JsonElement>()));
    }

    [Fact]
    public void ResolveBackground_wait_and_explicit_false()
    {
        Assert.False(IdeLifecycleJobs.ResolveBackground(new Dictionary<string, JsonElement>
        {
            ["wait"] = JsonSerializer.SerializeToElement(true)
        }));
        Assert.False(IdeLifecycleJobs.ResolveBackground(new Dictionary<string, JsonElement>
        {
            ["background"] = JsonSerializer.SerializeToElement(false)
        }));
        Assert.False(IdeLifecycleJobs.ResolveBackground(new Dictionary<string, JsonElement>
        {
            ["dry_run"] = JsonSerializer.SerializeToElement(true)
        }));
    }

    [Fact]
    public void ResolveDurable_deploy_defaults_true_build_requires_explicit()
    {
        Assert.True(IdeLifecycleJobs.ResolveDurable(new Dictionary<string, JsonElement>(), "deploy"));
        Assert.False(IdeLifecycleJobs.ResolveDurable(new Dictionary<string, JsonElement>(), "build"));
        Assert.True(IdeLifecycleJobs.ResolveDurable(new Dictionary<string, JsonElement>
        {
            ["durable"] = JsonSerializer.SerializeToElement(true)
        }, "build"));
        Assert.False(IdeLifecycleJobs.ResolveDurable(new Dictionary<string, JsonElement>
        {
            ["durable"] = JsonSerializer.SerializeToElement(false)
        }, "deploy"));
        Assert.False(IdeLifecycleJobs.ResolveDurable(new Dictionary<string, JsonElement>
        {
            ["dry_run"] = JsonSerializer.SerializeToElement(true)
        }, "deploy"));
    }

    [Fact]
    public void TryAutoArmLifecycle_arms_build_finished()
    {
        try
        {
            Assert.True(IdeLifecycleIgnite.TryAutoArm("build_finished", "build", "CdpMcp.csproj", enabled: true, out var armId));
            Assert.StartsWith(IdeLifecycleIgnite.BackgroundArmIdPrefix, armId!, StringComparison.Ordinal);
            var armed = IdeIgniteArmHost.Snapshot().FirstOrDefault(a => a.Id.Equals(armId, StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(armed);
            Assert.Equal("build_finished", armed!.Event);
        }
        finally
        {
            _ = IdeIgniteArmHost.Disarm(new Dictionary<string, JsonElement>
            {
                ["all"] = JsonSerializer.SerializeToElement(true),
                ["force"] = JsonSerializer.SerializeToElement(true)
            });
        }
    }

    [Fact]
    public async Task StartBuild_completes_and_last_returns_result()
    {
        var session = new SessionContext
        {
            ProjectRoot = Environment.CurrentDirectory,
            Language = "csharp"
        };
        var args = new Dictionary<string, JsonElement>
        {
            ["ignite_arm"] = JsonSerializer.SerializeToElement(false)
        };

        var startedJson = IdeLifecycleJobs.StartBuild(session, args, buildMod: null, new JsonSerializerOptions { WriteIndented = true });
        using (var doc = JsonDocument.Parse(startedJson))
        {
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("running", doc.RootElement.GetProperty("state").GetString());
        }

        var deadline = DateTime.UtcNow.AddSeconds(5);
        string? last = null;
        while (DateTime.UtcNow < deadline)
        {
            last = IdeLifecycleJobs.Last(new Dictionary<string, JsonElement> { ["kind"] = JsonSerializer.SerializeToElement("build") });
            using var probe = JsonDocument.Parse(last);
            if (probe.RootElement.TryGetProperty("state", out var st) && st.GetString() != "running")
                break;
            await Task.Delay(50);
        }

        Assert.NotNull(last);
        using var lastDoc = JsonDocument.Parse(last);
        Assert.NotEqual("running", lastDoc.RootElement.TryGetProperty("state", out var state) ? state.GetString() : null);
    }
}
