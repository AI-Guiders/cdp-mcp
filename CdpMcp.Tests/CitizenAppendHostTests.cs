#nullable enable
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenAppendHostTests
{
    [Fact]
    public void Route_append_parses_body()
    {
        var r = CitizenIntentRouter.RouteOne("append path=a.cs body=\"-tail\"");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Append, r.Verb);
        Assert.Equal("a.cs", r.Path);
        Assert.Equal("-tail", r.NewString);
    }

    [Fact]
    public void Route_append_path_empty_fails()
    {
        var r = CitizenIntentRouter.RouteOne("append body=\"x\"");
        Assert.False(r.Ok);
        Assert.Equal("append_path_empty", r.Reason);
    }

    [Fact]
    public void Route_append_body_empty_fails()
    {
        var r = CitizenIntentRouter.RouteOne("append path=a.cs");
        Assert.False(r.Ok);
        Assert.Equal("append_body_empty", r.Reason);
    }

    [Fact]
    public void Execute_append_suffixes_disk_via_buffer_gate()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-append-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var rel = "seed.txt";
        var full = Path.Combine(root, rel);
        File.WriteAllText(full, "head");

        var store = new DocumentBufferStore();
        IdeLanguageTools.BindDocumentStore(store);
        var prevRoot = IdeCockpitHostChannel.ProjectRootResolver;
        var landRoot = Path.Combine(Path.GetTempPath(), "cdp-append-land-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(landRoot);
        NavigationLandLatch.RootOverrideForTests = landRoot;
        IdeDeskSeats.EnsureDefaultsFromSettings();
        IdeDeskSeats.Clear();
        IdeDeskSeats.TryPlaceExplicit("forward", "browser");

        try
        {
            IdeCockpitHostChannel.ProjectRootResolver = () => root;
            var intent = "append path=" + rel + " body=\"-tail\"";
            var routes = new[] { CitizenIntentRouter.RouteOne(intent) };
            Assert.Equal(CitizenIntentRouter.Verb.Append, routes[0].Verb);
            Assert.True(routes[0].Ok);
            Assert.Equal("-tail", routes[0].NewString);

            var applied = CitizenRouteHost.Execute(routes);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("append", applied[0].Action);
            Assert.Equal("editor_scene", applied[0].Go);
            Assert.Equal(Path.GetFullPath(full), applied[0].Path);

            Assert.Equal("head-tail", File.ReadAllText(full));

            Assert.True(File.Exists(NavigationLandLatch.LatchPath));
            using var land = System.Text.Json.JsonDocument.Parse(File.ReadAllText(NavigationLandLatch.LatchPath));
            Assert.Equal("open", land.RootElement.GetProperty("command").GetString());
            Assert.Equal(Path.GetFullPath(full), land.RootElement.GetProperty("path").GetString());
        }
        finally
        {
            IdeCockpitHostChannel.ProjectRootResolver = prevRoot;
            NavigationLandLatch.RootOverrideForTests = null;
            try { Directory.Delete(root, recursive: true); } catch { /* temp */ }
            try { Directory.Delete(landRoot, recursive: true); } catch { /* temp */ }
        }
    }
}
