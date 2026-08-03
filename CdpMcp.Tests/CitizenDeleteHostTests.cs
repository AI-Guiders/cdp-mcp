#nullable enable
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenDeleteHostTests
{
    [Fact]
    public void Route_delete_parses_path()
    {
        var r = CitizenIntentRouter.RouteOne("delete path=a.cs");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Delete, r.Verb);
        Assert.Equal("a.cs", r.Path);
        Assert.Null(r.Op);
    }

    [Fact]
    public void Route_rm_force_parses()
    {
        var r = CitizenIntentRouter.RouteOne("rm path=a.cs force=true");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Delete, r.Verb);
        Assert.Equal("a.cs", r.Path);
        Assert.Equal("force", r.Op);
    }

    [Fact]
    public void Route_delete_path_empty_fails()
    {
        var r = CitizenIntentRouter.RouteOne("delete force=true");
        Assert.False(r.Ok);
        Assert.Equal("delete_path_empty", r.Reason);
    }

    [Fact]
    public void Execute_delete_removes_disk_via_buffer_gate()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-delete-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var rel = "seed.txt";
        var full = Path.Combine(root, rel);
        File.WriteAllText(full, "gone-soon");

        var store = new DocumentBufferStore();
        IdeLanguageTools.BindDocumentStore(store);
        var prevRoot = IdeCockpitHostChannel.ProjectRootResolver;
        var landRoot = Path.Combine(Path.GetTempPath(), "cdp-delete-land-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(landRoot);
        NavigationLandLatch.RootOverrideForTests = landRoot;

        try
        {
            IdeCockpitHostChannel.ProjectRootResolver = () => root;
            var intent = "delete path=" + rel;
            var routes = new[] { CitizenIntentRouter.RouteOne(intent) };
            Assert.Equal(CitizenIntentRouter.Verb.Delete, routes[0].Verb);
            Assert.True(routes[0].Ok);

            var applied = CitizenRouteHost.Execute(routes);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("delete", applied[0].Action);
            Assert.Equal("buffer", applied[0].Go);
            Assert.Equal(Path.GetFullPath(full), applied[0].Path);

            Assert.False(File.Exists(full));

            Assert.True(File.Exists(NavigationLandLatch.LatchPath));
            using var land = System.Text.Json.JsonDocument.Parse(File.ReadAllText(NavigationLandLatch.LatchPath));
            Assert.Equal("close", land.RootElement.GetProperty("command").GetString());
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

    [Fact]
    public void Execute_delete_refuses_dirty_without_force()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-delete-dirty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var rel = "dirty.txt";
        var full = Path.Combine(root, rel);
        File.WriteAllText(full, "on-disk");

        var store = new DocumentBufferStore();
        IdeLanguageTools.BindDocumentStore(store);
        var prevRoot = IdeCockpitHostChannel.ProjectRootResolver;

        try
        {
            IdeCockpitHostChannel.ProjectRootResolver = () => root;
            var buf = store.Open(full);
            store.ApplySetText(buf, "dirty-in-mem");
            Assert.True(buf.Dirty);

            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("delete path=" + rel)
            ]);
            Assert.Single(applied);
            Assert.False(applied[0].Ok);
            Assert.Contains("dirty", applied[0].Reason ?? "", StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(full));

            var forced = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("delete path=" + rel + " force=true")
            ]);
            Assert.Single(forced);
            Assert.True(forced[0].Ok);
            Assert.False(File.Exists(full));
        }
        finally
        {
            IdeCockpitHostChannel.ProjectRootResolver = prevRoot;
            try { Directory.Delete(root, recursive: true); } catch { /* temp */ }
        }
    }
}
