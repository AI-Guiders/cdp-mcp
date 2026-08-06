#nullable enable
using System.Text.Json;
using Cdp.Core;
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
    public void Route_kb_world_query_is_search_session()
    {
        var r = CitizenIntentRouter.RouteOne("kb facet=world query=SoftFL invent REJECT");
        Assert.True(r.Ok);
        Assert.Equal("search_agent_notes", r.Op);
        Assert.Equal("memory_session", r.Server);
    }

    [Fact]
    public void Route_kb_facet_skill()
    {
        var r = CitizenIntentRouter.RouteOne("kb facet=skill list_pack");
        Assert.True(r.Ok);
        Assert.Equal("list_pack", r.Op);
        Assert.Equal("memory_skill", r.Server);
    }

    [Theory]
    [InlineData("kb facet=project", "list_knowledge_files", "memory_project")]
    [InlineData("kb facet=session", "memory_health", "memory_session")]
    [InlineData("kb facet=finding", "findings", "memory_self_finding")]
    [InlineData("kb facet=failure", "failures", "memory_self_failure")]
    [InlineData("kb facet=task", "route_next", "memory_task")]
    [InlineData("memory_project list_knowledge_files", "list_knowledge_files", "memory_project")]
    [InlineData("memory_session memory_health", "memory_health", "memory_session")]
    [InlineData("memory_self_finding findings", "findings", "memory_self_finding")]
    [InlineData("memory_self_failure failures", "failures", "memory_self_failure")]
    [InlineData("memory_task route_next", "route_next", "memory_task")]
    public void Route_kb_memory_facets_and_aliases(string raw, string op, string facet)
    {
        var r = CitizenIntentRouter.RouteOne(raw);
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Kb, r.Verb);
        Assert.Equal(op, r.Op);
        Assert.Equal(facet, r.Server);
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

    [Fact]
    public void Execute_kb_read_raw_markdown_body_is_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.KbCallOverride = (_, tool, _) =>
        {
            Assert.Equal("read_knowledge_file", tool);
            return Task.FromResult("# SHOWCASE\n\nHub map for agent-notes.");
        };
        try
        {
            var applied = CitizenRouteHost.Execute(
                [CitizenIntentRouter.RouteOne("kb read_knowledge_file file_path=SHOWCASE.md")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Contains("SHOWCASE", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Route_kb_search_token_binds_session()
    {
        var r = CitizenIntentRouter.RouteOne("kb search query=SoftFL");
        Assert.True(r.Ok);
        Assert.Equal("search_agent_notes", r.Op);
        Assert.Equal("memory_session", r.Server);
    }

    [Fact]
    public void Execute_kb_search_pulse_includes_match_hits()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.KbCallOverride = (_, tool, _) =>
        {
            Assert.Equal("search_agent_notes", tool);
            return Task.FromResult(
                "{\"query\":\"SoftFL\",\"total_matches\":2,\"returned_matches\":2,\"matches\":[{\"line\":10,\"text\":\"## SoftFL invent REJECT - dig before invent\"},{\"line\":40,\"text\":\"Face Done = operator Glass eyes only\"}]}");
        };
        try
        {
            var applied = CitizenRouteHost.Execute(
                [CitizenIntentRouter.RouteOne("kb search query=SoftFL")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok, applied[0].Reason);
            Assert.Contains("2 match(es)", applied[0].Pulse, StringComparison.Ordinal);
            Assert.Contains("SoftFL invent REJECT", applied[0].Pulse, StringComparison.Ordinal);
            Assert.Contains("q=SoftFL", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_kb_route_next_pulse_includes_toBe()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.KbCallOverride = (_, tool, _) =>
        {
            Assert.Equal("route_next", tool);
            return Task.FromResult(
                "{\"count\":2,\"next\":[{\"taskId\":\"t1\",\"title\":\"Thin\",\"toBe\":\"Surface next toBe in pulse\"},{\"taskId\":\"t2\",\"title\":\"Also\",\"toBe\":\"Keep SoftFL invent REJECT\"}]}");
        };
        try
        {
            var applied = CitizenRouteHost.Execute(
                [CitizenIntentRouter.RouteOne("kb facet=task route_next")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok, applied[0].Reason);
            Assert.Contains("2 hit(s)", applied[0].Pulse, StringComparison.Ordinal);
            Assert.Contains("Surface next toBe", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_kb_route_next_without_workspace_tips_cdp_open()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.SessionResolver = () => new SessionContext();
        CitizenRouteHost.ByDomainResolver = () => new Dictionary<string, ICdpBackendModule>(StringComparer.Ordinal)
        {
            [Cdp.Core.CdpDomains.MemoryTask] = new FakeKbModule(),
        };
        try
        {
            var applied = CitizenRouteHost.Execute(
                [CitizenIntentRouter.RouteOne("kb facet=task route_next")]);
            Assert.Single(applied);
            Assert.False(applied[0].Ok);
            Assert.Equal("kb_workspace_required · cdp_open", applied[0].Reason);
            Assert.Contains("need cdp_open", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    sealed class FakeKbModule : ICdpBackendModule
    {
        public string Domain => Cdp.Core.CdpDomains.MemoryTask;
        public bool IsEnabled => true;
        public string HealthSummary => "fake-kb";
        public IReadOnlyList<ToolAffordance> Affordances => [];

        public ValueTask<string> CallAsync(string tool, IReadOnlyDictionary<string, JsonElement> args) =>
            throw new InvalidOperationException("backend should not run without workspace");
    }
}
