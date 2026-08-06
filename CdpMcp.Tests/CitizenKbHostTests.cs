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

    [Fact]
    public void Execute_kb_findings_pulse_includes_entry_summary()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.KbCallOverride = (_, tool, _) =>
        {
            Assert.Equal("findings", tool);
            return Task.FromResult(
                "{\"count\":2,\"entries\":[{\"id\":\"f1\",\"path\":\"CitizenRouteHost.Kb.cs\",\"summary\":\"Thin findings pulse SoftFL invent risk\"},{\"id\":\"f2\",\"path\":\"x.cs\",\"summary\":\"Also surface path when summary missing\"}]}");
        };
        try
        {
            var applied = CitizenRouteHost.Execute(
                [CitizenIntentRouter.RouteOne("kb facet=finding findings")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok, applied[0].Reason);
            Assert.Contains("2 hit(s)", applied[0].Pulse, StringComparison.Ordinal);
            Assert.Contains("Thin findings pulse SoftFL", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_kb_failures_pulse_includes_errorOrMiss()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.KbCallOverride = (_, tool, _) =>
        {
            Assert.Equal("failures", tool);
            return Task.FromResult(
                "{\"count\":1,\"entries\":[{\"id\":\"e1\",\"tool\":\"findings\",\"errorOrMiss\":\"workspace_path is required\",\"why\":\"preflight missing\"}]}");
        };
        try
        {
            var applied = CitizenRouteHost.Execute(
                [CitizenIntentRouter.RouteOne("kb facet=failure failures")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok, applied[0].Reason);
            Assert.Contains("1 hit(s)", applied[0].Pulse, StringComparison.Ordinal);
            Assert.Contains("workspace_path is required", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_kb_memory_health_pulse_includes_level()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.KbCallOverride = (_, tool, _) =>
        {
            Assert.Equal("memory_health", tool);
            return Task.FromResult(
                "{\"health_level\":\"warning\",\"recommend_compaction\":true,\"hot_context\":{\"chars\":42000},\"warnings\":[\"hot_context_over_warning_budget\"]}");
        };
        try
        {
            var applied = CitizenRouteHost.Execute(
                [CitizenIntentRouter.RouteOne("kb facet=session memory_health")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok, applied[0].Reason);
            Assert.Contains("warning", applied[0].Pulse, StringComparison.Ordinal);
            Assert.Contains("hot=42000", applied[0].Pulse, StringComparison.Ordinal);
            Assert.Contains("compact?", applied[0].Pulse, StringComparison.Ordinal);
            Assert.Contains("hot_context_over_warning", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_kb_list_knowledge_files_pulse_includes_paths()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.KbCallOverride = (_, tool, _) =>
        {
            Assert.Equal("list_knowledge_files", tool);
            return Task.FromResult(
                "{\"path\":\"/kb\",\"total\":2,\"files\":[{\"path\":\"projects/a.md\",\"size_bytes\":10},{\"path\":\"META/b.md\",\"size_bytes\":20}]}");
        };
        try
        {
            var applied = CitizenRouteHost.Execute(
                [CitizenIntentRouter.RouteOne("kb facet=project list_knowledge_files")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok, applied[0].Reason);
            Assert.Contains("2 file(s)", applied[0].Pulse, StringComparison.Ordinal);
            Assert.Contains("projects/a.md", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_kb_read_hot_context_pulse_includes_sections()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.KbCallOverride = (_, tool, _) =>
        {
            Assert.Equal("read_hot_context", tool);
            return Task.FromResult(
                "{\"active_scope\":\"door-to-singularity\",\"loaded_sections\":[\"META/integrity-core\",\"domains/agent-operations\"],\"content\":\"<!-- section:META/integrity-core -->\\nbody\\n\"}");
        };
        try
        {
            var applied = CitizenRouteHost.Execute(
                [CitizenIntentRouter.RouteOne("kb facet=session read_hot_context")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok, applied[0].Reason);
            Assert.Contains("scope=door-to-singularity", applied[0].Pulse, StringComparison.Ordinal);
            Assert.Contains("2 section(s)", applied[0].Pulse, StringComparison.Ordinal);
            Assert.Contains("META/integrity-core", applied[0].Pulse, StringComparison.Ordinal);
            Assert.Contains("chars=", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_kb_knowledge_tags_pulse_includes_tags()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.KbCallOverride = (_, tool, _) =>
        {
            Assert.Equal("knowledge_tags", tool);
            return Task.FromResult(
                "{\"mode\":\"inventory\",\"total_tags\":3,\"tagged_files\":10,\"tags\":[{\"tag\":\"#ssot\",\"file_count\":5},{\"tag\":\"#integrity\",\"file_count\":2}]}");
        };
        try
        {
            var applied = CitizenRouteHost.Execute(
                [CitizenIntentRouter.RouteOne("kb facet=world knowledge_tags")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok, applied[0].Reason);
            Assert.Contains("3 tag(s)", applied[0].Pulse, StringComparison.Ordinal);
            Assert.Contains("files=10", applied[0].Pulse, StringComparison.Ordinal);
            Assert.Contains("#ssot", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_kb_route_context_without_query_tips_query()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.SessionResolver = () => new SessionContext
        {
            ProjectRoot = @"D:\Experiments\agent-notes",
        };
        CitizenRouteHost.ByDomainResolver = () => new Dictionary<string, ICdpBackendModule>(StringComparer.Ordinal)
        {
            [Cdp.Core.CdpDomains.MemorySession] = new FakeSessionKbModule(),
        };
        try
        {
            var applied = CitizenRouteHost.Execute(
                [CitizenIntentRouter.RouteOne("kb facet=session route_context")]);
            Assert.Single(applied);
            Assert.False(applied[0].Ok);
            Assert.Equal("query is required", applied[0].Reason);
            Assert.Contains("need query=", applied[0].Pulse, StringComparison.Ordinal);
            Assert.DoesNotContain("need cdp_open", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_kb_route_context_pulse_includes_selected()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.KbCallOverride = (_, tool, _) =>
        {
            Assert.Equal("route_context", tool);
            return Task.FromResult(
                "{\"query\":\"integrity\",\"resolved_scope\":\"door-to-singularity\",\"selected_count\":2,\"selected\":[{\"id\":\"META/integrity-core\",\"score\":40,\"match_count\":2,\"chars\":100,\"lines\":5,\"preview\":\"harm\"},{\"id\":\"domains/agent-operations\",\"score\":20,\"match_count\":1,\"chars\":50,\"lines\":2,\"preview\":\"dig\"}],\"assembled_context\":\"<!-- section:META/integrity-core -->\\nbody\\n\"}");
        };
        try
        {
            var applied = CitizenRouteHost.Execute(
                [CitizenIntentRouter.RouteOne("kb facet=session route_context query=integrity")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok, applied[0].Reason);
            Assert.Contains("2 selected", applied[0].Pulse, StringComparison.Ordinal);
            Assert.Contains("META/integrity-core", applied[0].Pulse, StringComparison.Ordinal);
            Assert.Contains("scope=door-to-singularity", applied[0].Pulse, StringComparison.Ordinal);
            Assert.Contains("chars=", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_kb_tasks_pulse_includes_toBe()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.KbCallOverride = (_, tool, _) =>
        {
            Assert.Equal("tasks", tool);
            return Task.FromResult(
                "{\"count\":2,\"tasks\":[{\"taskId\":\"t1\",\"title\":\"Thin\",\"toBe\":\"Surface tasks toBe in pulse\"},{\"taskId\":\"t2\",\"title\":\"Also\",\"toBe\":\"Keep SoftFL invent REJECT\"}]}");
        };
        try
        {
            var applied = CitizenRouteHost.Execute(
                [CitizenIntentRouter.RouteOne("kb facet=task tasks")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok, applied[0].Reason);
            Assert.Contains("2 hit(s)", applied[0].Pulse, StringComparison.Ordinal);
            Assert.Contains("Surface tasks toBe", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_kb_ensure_store_wrong_facet_remaps_to_task()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.KbCallOverride = (facet, tool, _) =>
        {
            Assert.Equal(Cdp.Core.CdpDomains.MemoryTask, facet);
            Assert.Equal("ensure_store", tool);
            return Task.FromResult(
                "{\"ok\":true,\"meta\":{\"storeDir\":\"D:\\\\Experiments\\\\agent-notes\\\\.task-knowledge\",\"resolvedScope\":\"default\"}}");
        };
        try
        {
            var route = CitizenIntentRouter.RouteOne("kb facet=session ensure_store");
            Assert.True(route.Ok, route.Reason);
            Assert.Equal(Cdp.Core.CdpDomains.MemoryTask, route.Server);
            var applied = CitizenRouteHost.Execute([route]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok, applied[0].Reason);
            Assert.Contains("store=", applied[0].Pulse, StringComparison.Ordinal);
            Assert.DoesNotContain("kb_tool_unknown", applied[0].Reason ?? "", StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_kb_read_card_without_path_tips_relative_path()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.SessionResolver = () => new SessionContext
        {
            ProjectRoot = @"D:\Experiments\agent-notes",
        };
        CitizenRouteHost.ByDomainResolver = () => new Dictionary<string, ICdpBackendModule>(StringComparer.Ordinal)
        {
            [Cdp.Core.CdpDomains.MemoryTask] = new FakeKbModule(),
        };
        try
        {
            var applied = CitizenRouteHost.Execute(
                [CitizenIntentRouter.RouteOne("kb facet=task read_card")]);
            Assert.Single(applied);
            Assert.False(applied[0].Ok);
            Assert.Equal("relative_path is required", applied[0].Reason);
            Assert.Contains("need relative_path=", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_kb_man_raw_text_pulses_body_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.KbCallOverride = (_, tool, _) =>
        {
            Assert.Equal("man", tool);
            return Task.FromResult(
                "TaskKnowledge man\nTools: ensure_store, route_next, tasks, read_card, write_card\nUse relative_path= for cards.");
        };
        try
        {
            var applied = CitizenRouteHost.Execute(
                [CitizenIntentRouter.RouteOne("kb facet=task man")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok, applied[0].Reason);
            Assert.Contains("body", applied[0].Pulse, StringComparison.Ordinal);
            Assert.Contains("TaskKnowledge man", applied[0].Pulse, StringComparison.Ordinal);
            Assert.Contains("ensure_store", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_kb_get_process_without_id_tips_process_id()
    {
        CitizenRouteHost.UnbindLifecycle();
        try
        {
            var applied = CitizenRouteHost.Execute(
                [CitizenIntentRouter.RouteOne("kb facet=world get_process")]);
            Assert.Single(applied);
            Assert.False(applied[0].Ok);
            Assert.Equal("kb_process_id_required", applied[0].Reason);
            Assert.Contains("need process_id=", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_kb_man_wrong_facet_tips_unknown_with_homes()
    {
        CitizenRouteHost.UnbindLifecycle();
        try
        {
            var route = CitizenIntentRouter.RouteOne("kb facet=world man");
            Assert.False(route.Ok);
            Assert.Equal("kb_tool_unknown", route.Reason);
            Assert.Equal("man", route.Op);
            Assert.Equal(Cdp.Core.CdpDomains.MemoryWorld, route.Server);
            var applied = CitizenRouteHost.Execute([route]);
            Assert.Single(applied);
            Assert.False(applied[0].Ok);
            Assert.Contains("man unknown", applied[0].Pulse, StringComparison.Ordinal);
            Assert.Contains("facet=task|finding|failure", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_kb_validate_sections_pulse_includes_section_ids()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.KbCallOverride = (_, tool, _) =>
        {
            Assert.Equal("validate_sections", tool);
            return Task.FromResult(
                "{\"ok\":true,\"summary\":\"clean\",\"section_ids\":[\"baseline-integrity\",\"agent-equal-standing\",\"hot-context\"],\"open_marker_count\":3,\"close_marker_count\":3,\"complete_block_count\":3,\"duplicates\":[],\"problems\":[]}");
        };
        try
        {
            var applied = CitizenRouteHost.Execute(
                [CitizenIntentRouter.RouteOne("kb facet=session validate_sections")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok, applied[0].Reason);
            Assert.Contains("3 section(s)", applied[0].Pulse, StringComparison.Ordinal);
            Assert.Contains("baseline-integrity", applied[0].Pulse, StringComparison.Ordinal);
            Assert.Contains("agent-equal-standing", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_kb_normalize_sections_pulse_includes_changed()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.KbCallOverride = (_, tool, _) =>
        {
            Assert.Equal("normalize_sections", tool);
            return Task.FromResult(
                "{\"changed\":true,\"before\":{\"section_ids\":[\"a\"]},\"after\":{\"section_ids\":[\"a\"]},\"content\":\"## a\\nbody\"}");
        };
        try
        {
            var applied = CitizenRouteHost.Execute(
                [CitizenIntentRouter.RouteOne("kb facet=session normalize_sections")]);
            Assert.Single(applied);
            Assert.Contains("changed", applied[0].Pulse, StringComparison.Ordinal);
            Assert.Contains("chars=", applied[0].Pulse, StringComparison.Ordinal);
            Assert.DoesNotContain("section(s)", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_kb_finding_check_without_path_tips_path()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.SessionResolver = () => new SessionContext
        {
            ProjectRoot = @"D:\Experiments\agent-notes",
        };
        CitizenRouteHost.ByDomainResolver = () => new Dictionary<string, ICdpBackendModule>(StringComparer.Ordinal)
        {
            [Cdp.Core.CdpDomains.MemorySelfFinding] = new ThrowingArgKbModule(
                Cdp.Core.CdpDomains.MemorySelfFinding, "path is required."),
        };
        try
        {
            var applied = CitizenRouteHost.Execute(
                [CitizenIntentRouter.RouteOne("kb facet=finding finding_check")]);
            Assert.Single(applied);
            Assert.False(applied[0].Ok);
            Assert.Contains("need path=", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_kb_analytics_upsert_without_id_tips_analytics_id()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.SessionResolver = () => new SessionContext
        {
            ProjectRoot = @"D:\Experiments\agent-notes",
        };
        CitizenRouteHost.ByDomainResolver = () => new Dictionary<string, ICdpBackendModule>(StringComparer.Ordinal)
        {
            [Cdp.Core.CdpDomains.MemoryTask] = new ThrowingArgKbModule(
                Cdp.Core.CdpDomains.MemoryTask, "analytics_id is required."),
        };
        try
        {
            var applied = CitizenRouteHost.Execute(
                [CitizenIntentRouter.RouteOne("kb facet=task analytics_upsert")]);
            Assert.Single(applied);
            Assert.False(applied[0].Ok);
            Assert.Contains("need analytics_id=", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_kb_upsert_section_notes_without_id_tips_section_id()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.SessionResolver = () => new SessionContext
        {
            ProjectRoot = @"D:\Experiments\agent-notes",
        };
        CitizenRouteHost.ByDomainResolver = () => new Dictionary<string, ICdpBackendModule>(StringComparer.Ordinal)
        {
            [Cdp.Core.CdpDomains.MemorySession] = new ThrowingArgKbModule(
                Cdp.Core.CdpDomains.MemorySession, "section_id is required."),
        };
        try
        {
            var applied = CitizenRouteHost.Execute(
                [CitizenIntentRouter.RouteOne("kb facet=session upsert_agent_notes_section")]);
            Assert.Single(applied);
            Assert.False(applied[0].Ok);
            Assert.Contains("need section_id=", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_kb_failure_record_without_tool_tips_tool()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.SessionResolver = () => new SessionContext
        {
            ProjectRoot = @"D:\Experiments\agent-notes",
        };
        CitizenRouteHost.ByDomainResolver = () => new Dictionary<string, ICdpBackendModule>(StringComparer.Ordinal)
        {
            [Cdp.Core.CdpDomains.MemorySelfFailure] = new ThrowingArgKbModule(
                Cdp.Core.CdpDomains.MemorySelfFailure, "tool is required."),
        };
        try
        {
            var applied = CitizenRouteHost.Execute(
                [CitizenIntentRouter.RouteOne("kb facet=failure failure_record")]);
            Assert.Single(applied);
            Assert.False(applied[0].Ok);
            Assert.Contains("need tool=", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Route_kb_failure_record_tool_arg_keeps_failure_record_op()
    {
        var r = CitizenIntentRouter.RouteOne("kb facet=failure failure_record tool=cdp_test");
        Assert.True(r.Ok, r.Reason);
        Assert.Equal("failure_record", r.Op);
        Assert.Equal(Cdp.Core.CdpDomains.MemorySelfFailure, r.Server);
    }

    [Fact]
    public void Execute_kb_task_upsert_without_id_tips_task_id()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.SessionResolver = () => new SessionContext
        {
            ProjectRoot = @"D:\Experiments\agent-notes",
        };
        CitizenRouteHost.ByDomainResolver = () => new Dictionary<string, ICdpBackendModule>(StringComparer.Ordinal)
        {
            [Cdp.Core.CdpDomains.MemoryTask] = new ThrowingArgKbModule(
                Cdp.Core.CdpDomains.MemoryTask, "task_id is required."),
        };
        try
        {
            var applied = CitizenRouteHost.Execute(
                [CitizenIntentRouter.RouteOne("kb facet=task task_upsert")]);
            Assert.Single(applied);
            Assert.False(applied[0].Ok);
            Assert.Contains("need task_id=", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_kb_write_card_without_content_tips_content()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.SessionResolver = () => new SessionContext
        {
            ProjectRoot = @"D:\Experiments\agent-notes",
        };
        CitizenRouteHost.ByDomainResolver = () => new Dictionary<string, ICdpBackendModule>(StringComparer.Ordinal)
        {
            [Cdp.Core.CdpDomains.MemoryTask] = new ThrowingArgKbModule(
                Cdp.Core.CdpDomains.MemoryTask, "content is required."),
        };
        try
        {
            var applied = CitizenRouteHost.Execute(
                [CitizenIntentRouter.RouteOne("kb facet=task write_card relative_path=tasks/x.md")]);
            Assert.Single(applied);
            Assert.False(applied[0].Ok);
            Assert.Contains("need content=", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_kb_finding_check_pulse_includes_advice()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.KbCallOverride = (_, tool, _) =>
        {
            Assert.Equal("finding_check", tool);
            return Task.FromResult(
                "{\"path\":\"CitizenRouteHost.Kb.cs\",\"currentHash\":\"abc\",\"memo\":null,\"hashMatch\":false,\"depsOk\":null,\"staleDeps\":null,\"advice\":\"reread_file\"}");
        };
        try
        {
            var applied = CitizenRouteHost.Execute(
                [CitizenIntentRouter.RouteOne("kb facet=finding finding_check path=CitizenRouteHost.Kb.cs")]);
            Assert.Single(applied);
            Assert.Contains("advice=reread_file", applied[0].Pulse, StringComparison.Ordinal);
            Assert.Contains("path=CitizenRouteHost.Kb.cs", applied[0].Pulse, StringComparison.Ordinal);
            Assert.Contains("hash_miss", applied[0].Pulse, StringComparison.Ordinal);
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

    sealed class ThrowingArgKbModule(string domain, string message) : ICdpBackendModule
    {
        public string Domain => domain;
        public bool IsEnabled => true;
        public string HealthSummary => "fake-throw-arg";
        public IReadOnlyList<ToolAffordance> Affordances => [];

        public ValueTask<string> CallAsync(string tool, IReadOnlyDictionary<string, JsonElement> args) =>
            throw new ArgumentException(message);
    }

    sealed class FakeSessionKbModule : ICdpBackendModule
    {
        public string Domain => Cdp.Core.CdpDomains.MemorySession;
        public bool IsEnabled => true;
        public string HealthSummary => "fake-session-kb";
        public IReadOnlyList<ToolAffordance> Affordances => [];

        public ValueTask<string> CallAsync(string tool, IReadOnlyDictionary<string, JsonElement> args) =>
            throw new InvalidOperationException("backend should not run without query");
    }
}
