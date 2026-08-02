#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenKbHostTests
{
    [Fact]
    public void Route_kb_alone_is_list_pack_world()
    {
        var r = CitizenIntentRouter.RouteOne("kb");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Kb, r.Verb);
        Assert.Equal("list_pack", r.Op);
        Assert.Equal("memory_world", r.Server);
        Assert.Equal("kb", r.Go);
    }

    [Fact]
    public void Route_kb_get_definition_parses()
    {
        var r = CitizenIntentRouter.RouteOne("kb get_definition definition_id=debug-radius");
        Assert.True(r.Ok);
        Assert.Equal("get_definition", r.Op);
        Assert.Equal("memory_world", r.Server);
    }

    [Fact]
    public void Route_kb_process_requires_id()
    {
        var r = CitizenIntentRouter.RouteOne("kb get_process");
        Assert.False(r.Ok);
        Assert.Equal("kb_process_id_required", r.Reason);
    }

    [Fact]
    public void Route_kb_facet_skill()
    {
        var r = CitizenIntentRouter.RouteOne("kb facet=skill list_pack");
        Assert.True(r.Ok);
        Assert.Equal("list_pack", r.Op);
        Assert.Equal("memory_skill", r.Server);
    }

    [Fact]
    public void Execute_kb_without_backend_fails_disabled()
    {
        CitizenRouteHost.UnbindLifecycle();
        var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("kb")]);
        Assert.Single(applied);
        Assert.False(applied[0].Ok);
        Assert.Equal("kb", applied[0].Action);
        Assert.StartsWith("kb_facet_disabled:", applied[0].Reason);
    }

    [Fact]
    public void Execute_kb_get_definition_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.KbCallOverride = (_, tool, args) =>
        {
            Assert.Equal("get_definition", tool);
            Assert.True(args.ContainsKey("definition_id"));
            return Task.FromResult("""{"ok":true,"definition_id":"debug-radius","pack_id":"epistemic-scene","llm_cue":"shrink"}""");
        };
        try
        {
            var applied = CitizenRouteHost.Execute(
                [CitizenIntentRouter.RouteOne("kb get_definition definition_id=debug-radius")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("kb", applied[0].Action);
            Assert.Contains("debug-radius", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
