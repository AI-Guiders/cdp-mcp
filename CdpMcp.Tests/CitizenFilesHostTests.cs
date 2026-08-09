#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenFilesHostTests
{
    [Fact]
    public void Route_files_alone_is_scene()
    {
        var r = CitizenIntentRouter.RouteOne("files");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Files, r.Verb);
        Assert.Equal("scene", r.Op);
        Assert.Equal("files_desk", r.Go);
    }

    [Fact]
    public void Route_desk_cdp_and_compounds()
    {
        var desk = CitizenIntentRouter.RouteOne("files_desk");
        Assert.True(desk.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Files, desk.Verb);
        Assert.Equal("scene", desk.Op);

        var cdp = CitizenIntentRouter.RouteOne("cdp_files");
        Assert.True(cdp.Ok);
        Assert.Equal("scene", cdp.Op);

        var list = CitizenIntentRouter.RouteOne("files_list");
        Assert.True(list.Ok);
        Assert.Equal("list", list.Op);

        var tree = CitizenIntentRouter.RouteOne("files tree depth=2");
        Assert.True(tree.Ok);
        Assert.Equal("tree", tree.Op);

        var open = CitizenIntentRouter.RouteOne("files_open path=README.md");
        Assert.True(open.Ok);
        Assert.Equal("open", open.Op);
        Assert.Equal("README.md", open.Path);
    }

    [Fact]
    public void Route_no_steal_bare_list_open_search_read()
    {
        Assert.NotEqual(CitizenIntentRouter.Verb.Files, CitizenIntentRouter.RouteOne("list").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Files, CitizenIntentRouter.RouteOne("open path=x.md").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Files, CitizenIntentRouter.RouteOne("search query=foo").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Files, CitizenIntentRouter.RouteOne("find query=foo").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Files, CitizenIntentRouter.RouteOne("read").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Files, CitizenIntentRouter.RouteOne("scene").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Files, CitizenIntentRouter.RouteOne("cd").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Files, CitizenIntentRouter.RouteOne("clear").Verb);
    }

    [Fact]
    public void Route_files_unknown_op_fails()
    {
        var r = CitizenIntentRouter.RouteOne("files boom");
        Assert.False(r.Ok);
        Assert.Equal("files_op_unknown", r.Reason);
    }

    [Fact]
    public void Execute_scene_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.FilesHandleOverride = _ =>
            """{"ok":true,"schema":"files/v1","pulse":"files · project · cdp-mcp · 12"}""";
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("files")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("files", applied[0].Action);
            Assert.Contains("files", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.FilesHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_entries_ship_into_applied_and_peer_event()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenPeerAck.ResetForTests();
        CitizenRouteHost.FilesHandleOverride = _ =>
            """{"ok":true,"schema":"files/v1","cwd":"D:/Experiments/agent-notes/knowledge","pulse":"files · external · knowledge · 37","total":37,"entries":[{"kind":"dir","name":"domains","path":"D:/Experiments/agent-notes/knowledge/domains"},{"kind":"file","name":"README.md","path":"D:/Experiments/agent-notes/knowledge/README.md"}]}""";
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("files")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.False(string.IsNullOrWhiteSpace(applied[0].Ship));
            Assert.Contains("cwd | ", applied[0].Ship, StringComparison.Ordinal);
            Assert.Contains("dir  domains", applied[0].Ship, StringComparison.Ordinal);
            Assert.Contains("file README.md", applied[0].Ship, StringComparison.Ordinal);
            Assert.Contains("ship=", applied[0].Pulse, StringComparison.Ordinal);

            var ack = CitizenPeerAck.FromExecuted(applied);
            Assert.Contains("ship  |", ack.Event, StringComparison.Ordinal);
            Assert.Contains("domains", ack.Event, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.FilesHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
            CitizenPeerAck.ResetForTests();
        }
    }

    [Fact]
    public void Execute_list_passes_op_and_where()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.FilesHandleOverride = args =>
        {
            seen = args;
            return """{"ok":true,"op":"list","total":3,"pulse":"files list ok total=3"}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("files list where=project")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("list", seen!["op"].GetString());
            Assert.Equal("project", seen["where"].GetString());
        }
        finally
        {
            CitizenRouteHost.FilesHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_tree_passes_depth_as_number()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.FilesHandleOverride = args =>
        {
            seen = args;
            return """{"ok":true,"op":"tree","pulse":"files tree ok"}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("files tree depth=2")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("tree", seen!["op"].GetString());
            Assert.Equal(JsonValueKind.Number, seen["depth"].ValueKind);
            Assert.Equal(2, seen["depth"].GetInt32());
        }
        finally
        {
            CitizenRouteHost.FilesHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
