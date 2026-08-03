#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenIdeHostTests
{
    [Fact]
    public void Route_goto_requires_path()
    {
        var r = CitizenIntentRouter.RouteOne("goto");
        Assert.False(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Ide, r.Verb);
        Assert.Equal("ide_path_required", r.Reason);
    }

    [Fact]
    public void Route_goto_requires_line()
    {
        var r = CitizenIntentRouter.RouteOne("goto path=CitizenRouteHost.cs");
        Assert.False(r.Ok);
        Assert.Equal("ide_line_required", r.Reason);
    }

    [Fact]
    public void Route_goto_ok()
    {
        var r = CitizenIntentRouter.RouteOne("goto path=CitizenRouteHost.cs line=10 column=5");
        Assert.True(r.Ok);
        Assert.Equal("go_to_definition", r.Op);
        Assert.Equal("CitizenRouteHost.cs", r.Path);
    }

    [Fact]
    public void Route_ide_usages_ok()
    {
        var r = CitizenIntentRouter.RouteOne("ide usages path=X.cs line=2");
        Assert.True(r.Ok);
        Assert.Equal("find_usages", r.Op);
    }

    [Fact]
    public void Route_diagnostics_ok_without_line()
    {
        var r = CitizenIntentRouter.RouteOne("diagnostics path=X.cs");
        Assert.True(r.Ok);
        Assert.Equal("get_diagnostics", r.Op);
    }

    [Fact]
    public void Route_complete_requires_column()
    {
        var r = CitizenIntentRouter.RouteOne("complete path=X.cs line=10");
        Assert.False(r.Ok);
        Assert.Equal("ide_column_required", r.Reason);
    }

    [Fact]
    public void Route_complete_ok()
    {
        var r = CitizenIntentRouter.RouteOne("complete path=X.cs line=10 column=4 prefix=Run");
        Assert.True(r.Ok);
        Assert.Equal("get_completions", r.Op);
    }

    [Fact]
    public void Route_signature_ok()
    {
        var r = CitizenIntentRouter.RouteOne("signature path=X.cs line=12 column=8");
        Assert.True(r.Ok);
        Assert.Equal("get_signature_help", r.Op);
    }

    [Fact]
    public void Route_symbols_ok_without_line()
    {
        var r = CitizenIntentRouter.RouteOne("symbols path=X.cs");
        Assert.True(r.Ok);
        Assert.Equal("get_document_symbols", r.Op);
    }

    [Fact]
    public void Route_outline_stays_sniper()
    {
        var r = CitizenIntentRouter.RouteOne("outline path=X.cs");
        Assert.Equal(CitizenIntentRouter.Verb.Sniper, r.Verb);
    }

    [Fact]
    public void Route_symbol_requires_column()
    {
        var r = CitizenIntentRouter.RouteOne("symbol path=X.cs line=10");
        Assert.False(r.Ok);
        Assert.Equal("ide_column_required", r.Reason);
    }

    [Fact]
    public void Route_symbol_ok()
    {
        var r = CitizenIntentRouter.RouteOne("symbol path=X.cs line=10 column=4");
        Assert.True(r.Ok);
        Assert.Equal("get_symbol_at_position", r.Op);
    }

    [Fact]
    public void Route_rename_requires_new_name()
    {
        var r = CitizenIntentRouter.RouteOne("rename path=X.cs line=10 column=4");
        Assert.False(r.Ok);
        Assert.Equal("ide_new_name_required", r.Reason);
    }

    [Fact]
    public void Route_rename_ok()
    {
        var r = CitizenIntentRouter.RouteOne("rename path=X.cs line=10 column=4 new_name=Foo");
        Assert.True(r.Ok);
        Assert.Equal("rename_symbol", r.Op);
    }

    [Fact]
    public void Route_actions_ok()
    {
        var r = CitizenIntentRouter.RouteOne("actions path=X.cs line=10 column=4");
        Assert.True(r.Ok);
        Assert.Equal("code_actions", r.Op);
    }

    [Fact]
    public void Route_apply_action_requires_index()
    {
        var r = CitizenIntentRouter.RouteOne("apply_action path=X.cs line=10 column=4");
        Assert.False(r.Ok);
        Assert.Equal("ide_action_index_required", r.Reason);
    }

    [Fact]
    public void Route_peek_stays_sniper()
    {
        var r = CitizenIntentRouter.RouteOne("peek path=X.cs");
        Assert.Equal(CitizenIntentRouter.Verb.Sniper, r.Verb);
    }

    [Fact]
    public void Execute_ide_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.IdeCallOverride = (op, args) =>
        {
            Assert.Equal("go_to_definition", op);
            Assert.Equal("CitizenRouteHost.cs", args["file_path"].GetString());
            Assert.Equal(10, args["line"].GetInt32());
            return Task.FromResult("""{"locations":[{"path":"A.cs","line":1}]}""");
        };
        try
        {
            var applied = CitizenRouteHost.Execute(
                [CitizenIntentRouter.RouteOne("goto path=CitizenRouteHost.cs line=10")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("ide", applied[0].Action);
            Assert.Contains("loc", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_complete_passes_prefix_via_override()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.IdeCallOverride = (op, args) =>
        {
            Assert.Equal("get_completions", op);
            Assert.Equal("X.cs", args["file_path"].GetString());
            Assert.Equal(10, args["line"].GetInt32());
            Assert.Equal(4, args["column"].GetInt32());
            Assert.Equal("Run", args["prefix"].GetString());
            return Task.FromResult("""{"items":[{"label":"RunIde"},{"label":"RunFind"}]}""");
        };
        try
        {
            var applied = CitizenRouteHost.Execute(
                [CitizenIntentRouter.RouteOne("complete path=X.cs line=10 column=4 prefix=Run")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Contains("item", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_rename_passes_new_name_via_override()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.IdeCallOverride = (op, args) =>
        {
            Assert.Equal("rename_symbol", op);
            Assert.Equal("Foo", args["new_name"].GetString());
            Assert.False(args["apply"].GetBoolean());
            return Task.FromResult("""{"files":["X.cs"],"changes":[{"path":"X.cs"}]}""");
        };
        try
        {
            var applied = CitizenRouteHost.Execute(
                [CitizenIntentRouter.RouteOne("rename path=X.cs line=10 column=4 new_name=Foo apply=false")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Contains("rename", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
