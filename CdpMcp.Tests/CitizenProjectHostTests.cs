#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenProjectHostTests
{
    [Fact]
    public void Route_project_alone_is_scene()
    {
        var r = CitizenIntentRouter.RouteOne("project");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Project, r.Verb);
        Assert.Equal("scene", r.Op);
        Assert.Equal("project", r.Organ);
        Assert.Equal("project", r.Go);
    }

    [Fact]
    public void Route_sln_alone_is_list()
    {
        var r = CitizenIntentRouter.RouteOne("sln");
        Assert.True(r.Ok);
        Assert.Equal("list", r.Op);
        Assert.Equal("sln", r.Organ);
    }

    [Fact]
    public void Route_project_create_and_sln_projects()
    {
        var create = CitizenIntentRouter.RouteOne("project create output_dir=.cdp/scratch/tmp-proj template=classlib name=Tmp");
        Assert.True(create.Ok);
        Assert.Equal("create", create.Op);
        Assert.Equal(".cdp/scratch/tmp-proj", create.Path);
        Assert.Equal("Tmp", create.Tool);
        Assert.Equal("classlib", create.Detail);

        var miss = CitizenIntentRouter.RouteOne("project create");
        Assert.False(miss.Ok);
        Assert.Equal("project_output_dir_required", miss.Reason);

        var projects = CitizenIntentRouter.RouteOne("sln projects solution=CdpMcp.sln");
        Assert.True(projects.Ok);
        Assert.Equal("projects", projects.Op);
        Assert.Equal("CdpMcp.sln", projects.Scene);
    }

    [Fact]
    public void Route_unknown_and_compounds()
    {
        var bad = CitizenIntentRouter.RouteOne("project boom");
        Assert.False(bad.Ok);
        Assert.Equal("project_op_unknown", bad.Reason);

        var compound = CitizenIntentRouter.RouteOne("project_list root=.");
        Assert.True(compound.Ok);
        Assert.Equal("list", compound.Op);
        Assert.Equal(".", compound.Path);

        // Must not steal Ide project_root
        var root = CitizenIntentRouter.RouteOne("project_root");
        Assert.True(root.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Ide, root.Verb);
    }

    [Fact]
    public void Execute_project_scene_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        string? seenTool = null;
        CitizenRouteHost.ProjectDispatchOverride = (tool, _) =>
        {
            seenTool = tool;
            return """{"ok":true,"kind":"projects.scene","summary":"ok","pulse":"project scene ok"}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("project")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("project", applied[0].Action);
            Assert.Equal("cdp_project_scene", seenTool);
            Assert.Contains("project", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.ProjectDispatchOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_sln_list_passes_root()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        string? tool = null;
        CitizenRouteHost.ProjectDispatchOverride = (t, args) =>
        {
            tool = t;
            seen = args;
            return """{"ok":true,"kind":"solutions.list","summary":"ok"}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("sln list root=D:/work")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("cdp_sln_list", tool);
            Assert.NotNull(seen);
            Assert.Equal("D:/work", seen!["root"].GetString());
        }
        finally
        {
            CitizenRouteHost.ProjectDispatchOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
