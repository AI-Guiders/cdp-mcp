#nullable enable
using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenTestHostTests
{
    [Fact]
    public void Route_test_alone()
    {
        var r = CitizenIntentRouter.RouteOne("test");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Test, r.Verb);
        Assert.Equal("test", r.Go);
        Assert.Null(r.Path);
    }

    [Fact]
    public void Route_test_path()
    {
        var r = CitizenIntentRouter.RouteOne("test path=CdpMcp.Tests.csproj");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Test, r.Verb);
        Assert.Equal("CdpMcp.Tests.csproj", r.Path);
    }

    [Fact]
    public void Route_test_quoted_path_with_spaces()
    {
        var r = CitizenIntentRouter.RouteOne(
            """test path="D:/Experiments/Personal Cursor Folder/proj.csproj" filter=FullyQualifiedName~Smoke""");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Test, r.Verb);
        Assert.Equal("D:/Experiments/Personal Cursor Folder/proj.csproj", r.Path);
    }

    [Fact]
    public void Execute_test_without_session_fails_no_session()
    {
        CitizenRouteHost.UnbindLifecycle();
        var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("test")]);
        Assert.Single(applied);
        Assert.False(applied[0].Ok);
        Assert.Equal("test", applied[0].Action);
        Assert.Equal("no_session", applied[0].Reason);
    }

    [Fact]
    public void Execute_test_with_fake_module_ok()
    {
        var proj = Path.Combine(Path.GetTempPath(), "cdp-test-host-" + Guid.NewGuid().ToString("N") + ".csproj");
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
            CitizenRouteHost.BuildModuleResolver = () => new FakeTestModule();
            var applied = CitizenRouteHost.Execute(
                [CitizenIntentRouter.RouteOne("test path=" + proj + " filter=FullyQualifiedName~Smoke")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("test", applied[0].Action);
            Assert.Equal("test", applied[0].Go);
            Assert.Equal("test ok 1/1", applied[0].Pulse);
            Assert.False(string.IsNullOrWhiteSpace(applied[0].Seat));
        }
        finally
        {
            CitizenRouteHost.SessionResolver = prevSession;
            CitizenRouteHost.BuildModuleResolver = prevBuild;
            try { File.Delete(proj); } catch { /* temp */ }
        }
    }

    sealed class FakeTestModule : ICdpBackendModule
    {
        public string Domain => "build";
        public bool IsEnabled => true;
        public string HealthSummary => "fake-test";
        public IReadOnlyList<ToolAffordance> Affordances => [];

        public ValueTask<string> CallAsync(string underlyingName, IReadOnlyDictionary<string, JsonElement> args)
        {
            Assert.Equal("run_tests", underlyingName);
            Assert.True(args.ContainsKey("filter"));
            return ValueTask.FromResult("""{"ok":true,"exit_code":0,"pulse":"test ok 1/1"}""");
        }
    }
}
