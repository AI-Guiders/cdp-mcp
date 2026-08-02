#nullable enable
using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public sealed class CitizenBuildHostTests
{
    [Fact]
    public void Route_build_alone()
    {
        var r = CitizenIntentRouter.RouteOne("build");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Build, r.Verb);
        Assert.Equal("build", r.Go);
        Assert.Null(r.Path);
    }

    [Fact]
    public void Route_build_path()
    {
        var r = CitizenIntentRouter.RouteOne("build path=CdpMcp.csproj");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Build, r.Verb);
        Assert.Equal("CdpMcp.csproj", r.Path);
    }

    [Fact]
    public void Execute_build_without_session_fails_no_session()
    {
        CitizenRouteHost.UnbindLifecycle();
        var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("build")]);
        Assert.Single(applied);
        Assert.False(applied[0].Ok);
        Assert.Equal("build", applied[0].Action);
        Assert.Equal("no_session", applied[0].Reason);
    }

    [Fact]
    public void Execute_build_with_fake_module_ok()
    {
        var proj = Path.Combine(Path.GetTempPath(), "cdp-build-host-" + Guid.NewGuid().ToString("N") + ".csproj");
        File.WriteAllText(proj, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>\n");
        var session = new SessionContext
        {
            Language = "csharp",
            SolutionOrProjectPath = proj,
            ProjectRoot = Path.GetDirectoryName(proj)
        };

        var prevSession = CitizenRouteHost.SessionResolver;
        var prevBuild = CitizenRouteHost.BuildModuleResolver;
        IdeDeskSeats.EnsureDefaultsFromSettings();
        IdeDeskSeats.Clear();
        IdeDeskSeats.TryPlaceExplicit("forward", "browser");

        try
        {
            CitizenRouteHost.SessionResolver = () => session;
            CitizenRouteHost.BuildModuleResolver = () => new FakeBuildModule();
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("build path=" + proj)]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("build", applied[0].Action);
            Assert.Equal("build", applied[0].Go);
            Assert.Equal("ok", applied[0].Pulse);
            var map = IdeDeskSeats.Snapshot();
            Assert.Contains(map, kv => string.Equals(kv.Value, "build", StringComparison.OrdinalIgnoreCase)
                || string.Equals(kv.Value, "build_sa", StringComparison.OrdinalIgnoreCase)
                || string.Equals(kv.Value, "build_desk", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            CitizenRouteHost.SessionResolver = prevSession;
            CitizenRouteHost.BuildModuleResolver = prevBuild;
            try { File.Delete(proj); } catch { /* temp */ }
        }
    }

    sealed class FakeBuildModule : ICdpBackendModule
    {
        public string Domain => "build";
        public bool IsEnabled => true;
        public string HealthSummary => "fake-build";
        public IReadOnlyList<ToolAffordance> Affordances => [];

        public ValueTask<string> CallAsync(string underlyingName, IReadOnlyDictionary<string, JsonElement> args) =>
            ValueTask.FromResult("""{"ok":true,"exit_code":0,"pulse":"ok"}""");
    }
}
