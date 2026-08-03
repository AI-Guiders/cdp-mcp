#nullable enable
using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenRunHostTests
{
    [Fact]
    public void Route_run_alone()
    {
        var r = CitizenIntentRouter.RouteOne("run");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Run, r.Verb);
        Assert.Equal("run", r.Go);
        Assert.Null(r.Path);
    }

    [Fact]
    public void Route_run_path()
    {
        var r = CitizenIntentRouter.RouteOne("run path=CdpMcp.csproj");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Run, r.Verb);
        Assert.Equal("CdpMcp.csproj", r.Path);
    }

    [Fact]
    public void Route_dotnet_run_alias()
    {
        var r = CitizenIntentRouter.RouteOne("dotnet_run path=App.csproj");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Run, r.Verb);
        Assert.Equal("App.csproj", r.Path);
    }

    [Fact]
    public void Execute_run_without_session_fails_no_session()
    {
        CitizenRouteHost.UnbindLifecycle();
        var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("run")]);
        Assert.Single(applied);
        Assert.False(applied[0].Ok);
        Assert.Equal("run", applied[0].Action);
        Assert.Equal("no_session", applied[0].Reason);
    }

    [Fact]
    public void Execute_run_with_override_ok()
    {
        var proj = Path.Combine(Path.GetTempPath(), "cdp-run-host-" + Guid.NewGuid().ToString("N") + ".csproj");
        File.WriteAllText(proj, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>\n");
        var session = new SessionContext
        {
            Language = "csharp",
            SolutionOrProjectPath = proj,
            ProjectRoot = Path.GetDirectoryName(proj)
        };

        var prevSession = CitizenRouteHost.SessionResolver;
        var prevRun = CitizenRouteHost.RunLifecycleOverride;
        IdeDeskSeats.EnsureDefaultsFromSettings();
        IdeDeskSeats.Clear();
        IdeDeskSeats.TryPlaceExplicit("forward", "browser");

        try
        {
            CitizenRouteHost.SessionResolver = () => session;
            CitizenRouteHost.RunLifecycleOverride = (_, args, _) =>
            {
                Assert.True(args.ContainsKey("path"));
                Assert.Equal(proj, args["path"].GetString());
                return """{"ok":true,"exit_code":0,"pulse":"ok"}""";
            };
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("run path=" + proj)]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("run", applied[0].Action);
            Assert.Equal("run", applied[0].Go);
            Assert.Equal("ok", applied[0].Pulse);
            var map = IdeDeskSeats.Snapshot();
            Assert.Contains(map, kv => string.Equals(kv.Value, "run", StringComparison.OrdinalIgnoreCase)
                || string.Equals(kv.Value, "cdp_run", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            CitizenRouteHost.SessionResolver = prevSession;
            CitizenRouteHost.RunLifecycleOverride = prevRun;
            try { File.Delete(proj); } catch { /* temp */ }
        }
    }

    [Fact]
    public void TryResolveTarget_relative_path_joins_project_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-run-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var proj = Path.Combine(root, "App.csproj");
        File.WriteAllText(proj, "<Project />\n");
        var session = new SessionContext
        {
            Language = "csharp",
            ProjectRoot = root,
            SolutionOrProjectPath = proj
        };
        var args = new Dictionary<string, JsonElement>
        {
            ["path"] = JsonSerializer.SerializeToElement("App.csproj")
        };
        Assert.True(IdeSessionLifecycle.TryResolveTarget(session, args, out var target, out var err));
        Assert.True(string.IsNullOrEmpty(err));
        Assert.Equal(Path.GetFullPath(proj), target);
        try { Directory.Delete(root, recursive: true); } catch { /* temp */ }
    }

    [Fact]
    public void Execute_run_passes_no_build_and_configuration()
    {
        var session = new SessionContext
        {
            Language = "csharp",
            SolutionOrProjectPath = "CdpMcp.csproj",
            ProjectRoot = "."
        };
        var prevSession = CitizenRouteHost.SessionResolver;
        var prevRun = CitizenRouteHost.RunLifecycleOverride;
        try
        {
            CitizenRouteHost.SessionResolver = () => session;
            IReadOnlyDictionary<string, JsonElement>? seen = null;
            CitizenRouteHost.RunLifecycleOverride = (_, args, _) =>
            {
                seen = args;
                return """{"ok":true,"pulse":"ok"}""";
            };
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("run path=CdpMcp.csproj configuration=Release no_build=true")
            ]);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("Release", seen!["configuration"].GetString());
            Assert.True(seen["no_build"].GetBoolean());
        }
        finally
        {
            CitizenRouteHost.SessionResolver = prevSession;
            CitizenRouteHost.RunLifecycleOverride = prevRun;
        }
    }
}
