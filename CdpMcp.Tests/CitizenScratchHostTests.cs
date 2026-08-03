#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenScratchHostTests
{
    [Fact]
    public void Route_scratch_bare()
    {
        var r = CitizenIntentRouter.RouteOne("scratch");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Scratch, r.Verb);
        Assert.Equal("scratch", r.Op);
    }

    [Fact]
    public void Route_scratch_ext_and_text()
    {
        var r = CitizenIntentRouter.RouteOne("scratch ext=md text=hello");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Scratch, r.Verb);
        Assert.Equal("md", r.Detail);
        Assert.Equal("hello", r.NewString);
    }

    [Fact]
    public void Execute_scratch_passes_args_via_override()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.ScratchCallOverride = args =>
        {
            seen = args;
            return """{"schema":"editor_comfort/v0","ok":true,"op":"scratch","anchor":"[F:.cdp/scratch/untitled-1.cs]","meta":{"path":"D:\\tmp\\.cdp\\scratch\\untitled-1.cs","doc_id":"doc-1"}}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("scratch ext=cs text=\"// hi\"")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("scratch", applied[0].Action);
            Assert.NotNull(seen);
            Assert.Equal("scratch", seen!["op"].GetString());
            Assert.Equal("cs", seen["ext"].GetString());
            Assert.Equal("// hi", seen["text"].GetString());
            Assert.Contains("untitled-1.cs", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.ScratchCallOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_scratch_surfaces_failure()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.ScratchCallOverride = _ =>
            """{"schema":"editor_comfort/v0","ok":false,"op":"scratch","error":"no_project"}""";
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("scratch")
            ]);
            Assert.Single(applied);
            Assert.False(applied[0].Ok);
        }
        finally
        {
            CitizenRouteHost.ScratchCallOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
