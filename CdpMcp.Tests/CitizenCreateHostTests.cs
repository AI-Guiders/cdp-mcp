#nullable enable
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenCreateHostTests
{
    [Fact]
    public void Route_write_alias_is_create()
    {
        var r = CitizenIntentRouter.RouteOne("write path=a.cs text=\"hi\"");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Create, r.Verb);
        Assert.Equal("a.cs", r.Path);
        Assert.Equal("hi", r.NewString);
    }

    [Fact]
    public void Route_create_path_empty_fails()
    {
        var r = CitizenIntentRouter.RouteOne("create body=\"x\"");
        Assert.False(r.Ok);
        Assert.Equal("create_path_empty", r.Reason);
    }

    [Fact]
    public void Route_create_overwrite_flag()
    {
        var r = CitizenIntentRouter.RouteOne("create path=a.cs body=\"x\" overwrite=true");
        Assert.True(r.Ok);
        Assert.Equal("overwrite", r.Op);
    }

    [Fact]
    public void Execute_create_writes_disk_via_buffer_gate()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-create-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var rel = "fresh.txt";
        var full = Path.Combine(root, rel);

        var store = new DocumentBufferStore();
        IdeLanguageTools.BindDocumentStore(store);
        var prevRoot = IdeCockpitHostChannel.ProjectRootResolver;
        var landRoot = Path.Combine(Path.GetTempPath(), "cdp-create-land-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(landRoot);
        NavigationLandLatch.RootOverrideForTests = landRoot;
        IdeDeskSeats.EnsureDefaultsFromSettings();
        IdeDeskSeats.Clear();
        IdeDeskSeats.TryPlaceExplicit("forward", "browser");

        try
        {
            IdeCockpitHostChannel.ProjectRootResolver = () => root;
            var intent = "create path=" + rel + " body=\"wave-create\"";
            var routes = new[] { CitizenIntentRouter.RouteOne(intent) };
            Assert.Equal(CitizenIntentRouter.Verb.Create, routes[0].Verb);
            Assert.True(routes[0].Ok);
            Assert.Equal("wave-create", routes[0].NewString);

            var applied = CitizenRouteHost.Execute(routes);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("create", applied[0].Action);
            Assert.Equal("editor_scene", applied[0].Go);
            Assert.Equal(Path.GetFullPath(full), applied[0].Path);

            Assert.True(File.Exists(full));
            Assert.Contains("wave-create", File.ReadAllText(full), StringComparison.Ordinal);

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
