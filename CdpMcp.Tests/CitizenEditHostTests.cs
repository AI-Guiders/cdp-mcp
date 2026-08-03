#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenEditHostTests
{
    [Fact]
    public void Route_edit_anchor_parses()
    {
        var r = CitizenIntentRouter.RouteOne(
            "edit path=a.cs anchor=\"[F:a.cs;M:Foo]\" text=\"patched\" place=after");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Edit, r.Verb);
        Assert.Equal("a.cs", r.Path);
        Assert.Equal("[F:a.cs;M:Foo]", r.Detail);
        Assert.Equal("patched", r.NewString);
        Assert.Equal("after", r.Op);
        Assert.Equal("buffer", r.Go);
    }

    [Fact]
    public void Route_anchor_alias_defaults_place_replace()
    {
        var r = CitizenIntentRouter.RouteOne(
            "anchor path=a.cs at=\"[F:a.cs;M:Foo]\" body=\"x\"");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Edit, r.Verb);
        Assert.Equal("replace", r.Op);
        Assert.Equal("x", r.NewString);
    }

    [Fact]
    public void Route_edit_requires_path_anchor_text()
    {
        Assert.Equal("edit_path_required", CitizenIntentRouter.RouteOne("edit").Reason);
        Assert.Equal(
            "edit_anchor_required",
            CitizenIntentRouter.RouteOne("edit path=a.cs text=x").Reason);
        Assert.Equal(
            "edit_text_required",
            CitizenIntentRouter.RouteOne("edit path=a.cs anchor=\"[F:a.cs;M:Foo]\"").Reason);
    }

    [Fact]
    public void Route_edit_refuses_set_text()
    {
        var r = CitizenIntentRouter.RouteOne("edit path=a.cs edit_op=set_text text=bad");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Refuse, r.Verb);
        Assert.Contains("edit_refuse_set_text", r.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_edit_passes_anchor_args_via_override()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.EditCallOverride = args =>
        {
            seen = args;
            return """{"ok":true,"op":"anchor","meta":{"path":"D:\\tmp\\a.cs","doc_id":"doc-1"}}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne(
                    "edit path=a.cs anchor=\"[F:a.cs;M:Foo]\" text=\"hello\" place=before")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("edit", applied[0].Action);
            Assert.NotNull(seen);
            Assert.Equal("edit", seen!["op"].GetString());
            Assert.Equal("anchor", seen["edit_op"].GetString());
            Assert.Equal("a.cs", seen["path"].GetString());
            Assert.Equal("[F:a.cs;M:Foo]", seen["anchor"].GetString());
            Assert.Equal("hello", seen["text"].GetString());
            Assert.Equal("before", seen["place"].GetString());
            Assert.True(seen["flush"].GetBoolean());
        }
        finally
        {
            CitizenRouteHost.EditCallOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
