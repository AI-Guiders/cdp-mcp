#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenUndoHostTests
{
    [Fact]
    public void Route_undo_defaults()
    {
        var r = CitizenIntentRouter.RouteOne("undo");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Undo, r.Verb);
        Assert.Equal("undo", r.Op);
        Assert.Equal("buffer", r.Go);
    }

    [Fact]
    public void Route_undo_path()
    {
        var r = CitizenIntentRouter.RouteOne("undo path=a.cs");
        Assert.True(r.Ok);
        Assert.Equal("a.cs", r.Path);
        Assert.Equal("undo", r.Op);
    }

    [Fact]
    public void Route_redo_and_history()
    {
        var redo = CitizenIntentRouter.RouteOne("redo path=a.cs");
        Assert.True(redo.Ok);
        Assert.Equal("redo", redo.Op);

        var hist = CitizenIntentRouter.RouteOne("edit_history path=a.cs");
        Assert.True(hist.Ok);
        Assert.Equal("history", hist.Op);
    }

    [Fact]
    public void Execute_undo_passes_args_via_override()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.UndoCallOverride = args =>
        {
            seen = args;
            return """{"schema":"editor_comfort/v0","ok":true,"op":"undo","undone":"anchor","undo_left":0,"redo_left":1,"meta":{"path":"D:\\tmp\\a.cs","doc_id":"doc-1"}}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("undo path=a.cs")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("undo", applied[0].Action);
            Assert.NotNull(seen);
            Assert.Equal("undo", seen!["op"].GetString());
            Assert.Equal("a.cs", seen["path"].GetString());
            Assert.Contains("undo", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UndoCallOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_undo_surfaces_nothing_to_undo()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.UndoCallOverride = _ =>
            """{"schema":"editor_comfort/v0","ok":false,"op":"undo","error":"nothing_to_undo"}""";
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("undo path=a.cs")]);
            Assert.Single(applied);
            Assert.False(applied[0].Ok);
            Assert.Contains("nothing_to_undo", applied[0].Reason, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UndoCallOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
