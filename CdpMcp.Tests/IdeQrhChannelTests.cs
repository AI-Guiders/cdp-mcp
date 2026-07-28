using Xunit;

namespace CdpMcp.Tests;

public sealed class IdeQrhChannelTests
{
    static IdeChkChannel.ProbeCtx Ctx(
        string phase = "act",
        string? intent = null,
        bool taskOpen = true,
        bool igniteIdle = true,
        bool gitDirty = false,
        bool dapStopped = false) =>
        new(
            ProjectOpen: true,
            TaskOpen: taskOpen,
            IgniteIdle: igniteIdle,
            GitKnown: true,
            gitDirty,
            TestsGreen: false,
            TestsFailed: false,
            ProblemsClean: true,
            dapStopped,
            DapActive: dapStopped,
            SniperOk: true,
            phase,
            intent);

    [Fact]
    public void Suggest_dap_stopped_hot_page()
    {
        var s = IdeQrhChannel.SuggestFor(Ctx(dapStopped: true));
        Assert.Equal("dap-pdb-lock", s.HotId);
        Assert.Contains("dap-pdb-lock", s.Pulse);
    }

    [Fact]
    public void Suggest_ship_ecl_hot_binds_ship_dirty()
    {
        var ecl = IdeChkChannel.Build(Ctx(phase: "handoff", gitDirty: true));
        var s = IdeQrhChannel.SuggestFor(Ctx(phase: "handoff", gitDirty: true), ecl);
        Assert.Equal("ship-dirty", s.HotId);
    }

    [Fact]
    public void Open_page_returns_memory_and_steps()
    {
        var board = IdeQrhChannel.Handle(
            Ctx(),
            new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["op"] = System.Text.Json.JsonSerializer.SerializeToElement("open"),
                ["id"] = System.Text.Json.JsonSerializer.SerializeToElement("dap-pdb-lock")
            });
        var json = System.Text.Json.JsonSerializer.Serialize(board);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("qrh_organ/v0", root.GetProperty("schema").GetString());
        Assert.Equal("dap-pdb-lock", root.GetProperty("page").GetProperty("id").GetString());
        Assert.True(root.GetProperty("page").GetProperty("memory_items").GetArrayLength() > 0);
        Assert.True(root.GetProperty("page").GetProperty("steps").GetArrayLength() > 0);
    }

    [Fact]
    public void Search_pdb_hits_dap_page()
    {
        var board = IdeQrhChannel.Handle(
            Ctx(),
            new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["op"] = System.Text.Json.JsonSerializer.SerializeToElement("search"),
                ["q"] = System.Text.Json.JsonSerializer.SerializeToElement("pdb")
            });
        var json = System.Text.Json.JsonSerializer.Serialize(board);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var hits = doc.RootElement.GetProperty("hits");
        Assert.Contains(hits.EnumerateArray(), h => h.GetProperty("id").GetString() == "dap-pdb-lock");
    }

    [Fact]
    public void Suggest_review_ecl_related_includes_scm_via_desk()
    {
        var ecl = IdeChkChannel.Build(Ctx(phase: "review"));
        var s = IdeQrhChannel.SuggestFor(Ctx(phase: "review"), ecl);
        Assert.Contains("scm-via-desk", s.RelatedIds);
    }

    [Fact]
    public void Search_scm_via_desk_by_id()
    {
        var board = IdeQrhChannel.Handle(
            Ctx(),
            new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["op"] = System.Text.Json.JsonSerializer.SerializeToElement("search"),
                ["q"] = System.Text.Json.JsonSerializer.SerializeToElement("scm-via-desk")
            });
        var json = System.Text.Json.JsonSerializer.Serialize(board);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var hits = doc.RootElement.GetProperty("hits");
        Assert.Contains(hits.EnumerateArray(), h => h.GetProperty("id").GetString() == "scm-via-desk");
    }

    [Fact]
    public void Suggest_verify_phase_includes_test_via_desk()
    {
        var s = IdeQrhChannel.SuggestFor(Ctx(phase: "verify"));
        Assert.Contains("test-via-desk", s.RelatedIds);
    }

    [Fact]
    public void Search_test_via_desk_by_id()
    {
        var board = IdeQrhChannel.Handle(
            Ctx(),
            new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["op"] = System.Text.Json.JsonSerializer.SerializeToElement("search"),
                ["q"] = System.Text.Json.JsonSerializer.SerializeToElement("test-via-desk")
            });
        var json = System.Text.Json.JsonSerializer.Serialize(board);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var hits = doc.RootElement.GetProperty("hits");
        Assert.Contains(hits.EnumerateArray(), h => h.GetProperty("id").GetString() == "test-via-desk");
    }

    [Fact]
    public void Search_tool_result_tax_by_id()
    {
        var board = IdeQrhChannel.Handle(
            Ctx(),
            new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["op"] = System.Text.Json.JsonSerializer.SerializeToElement("search"),
                ["q"] = System.Text.Json.JsonSerializer.SerializeToElement("tool-result-tax")
            });
        var json = System.Text.Json.JsonSerializer.Serialize(board);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var hits = doc.RootElement.GetProperty("hits");
        Assert.Contains(hits.EnumerateArray(), h => h.GetProperty("id").GetString() == "tool-result-tax");
    }

    [Fact]
    public void Search_find_via_desk_by_id()
    {
        var board = IdeQrhChannel.Handle(
            Ctx(),
            new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["op"] = System.Text.Json.JsonSerializer.SerializeToElement("search"),
                ["q"] = System.Text.Json.JsonSerializer.SerializeToElement("find-via-desk")
            });
        var json = System.Text.Json.JsonSerializer.Serialize(board);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var hits = doc.RootElement.GetProperty("hits");
        Assert.Contains(hits.EnumerateArray(), h => h.GetProperty("id").GetString() == "find-via-desk");
    }

    [Fact]
    public void Suggest_act_includes_find_via_desk()
    {
        var s = IdeQrhChannel.SuggestFor(Ctx(phase: "act"));
        Assert.Contains("find-via-desk", s.RelatedIds);
    }

    [Fact]
    public void Suggest_act_without_task_focus_quiet_when_ignite_idle()
    {
        var s = IdeQrhChannel.SuggestFor(Ctx(phase: "act", taskOpen: false, igniteIdle: true));
        Assert.NotEqual("plateau-no-task", s.HotId);
    }

    [Fact]
    public void Suggest_act_without_task_focus_hot_when_ignite_armed()
    {
        var s = IdeQrhChannel.SuggestFor(Ctx(phase: "act", taskOpen: false, igniteIdle: false));
        Assert.Equal("plateau-no-task", s.HotId);
    }

    [Fact]
    public void Search_plateau_page_by_id()
    {
        var board = IdeQrhChannel.Handle(
            Ctx(),
            new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["op"] = System.Text.Json.JsonSerializer.SerializeToElement("search"),
                ["q"] = System.Text.Json.JsonSerializer.SerializeToElement("plateau-no-task")
            });
        var json = System.Text.Json.JsonSerializer.Serialize(board);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var hits = doc.RootElement.GetProperty("hits");
        Assert.Contains(hits.EnumerateArray(), h => h.GetProperty("id").GetString() == "plateau-no-task");
    }

    [Fact]
    public void Overlay_add_page_searchable_and_suggests_on_explore()
    {
        IdeSettingsStore.Unset(IdeQrhChannel.OverlayKey);
        try
        {
            var pageJson = """
                {
                  "id": "vague-criteria",
                  "shelf": "abnormal",
                  "title": "Vague ask — act without C/S",
                  "condition": "No success axes before act",
                  "signals": ["vague", "criteria"],
                  "memory_items": ["Check P/S/C"],
                  "steps": [{ "text": "Clarify axes before deliverable" }],
                  "related": ["intake-brief"],
                  "suggest": [
                    { "phases": ["explore", "clarify", "recall"], "score": 55 },
                    { "ecl": ["intake"], "score": 75 }
                  ]
                }
                """;
            var add = IdeQrhChannel.Handle(
                Ctx(),
                new Dictionary<string, System.Text.Json.JsonElement>
                {
                    ["op"] = System.Text.Json.JsonSerializer.SerializeToElement("add"),
                    ["page"] = System.Text.Json.JsonSerializer.SerializeToElement(pageJson)
                });
            var addJson = System.Text.Json.JsonSerializer.Serialize(add);
            using (var addDoc = System.Text.Json.JsonDocument.Parse(addJson))
                Assert.True(addDoc.RootElement.GetProperty("ok").GetBoolean());

            var board = IdeQrhChannel.Handle(
                Ctx(),
                new Dictionary<string, System.Text.Json.JsonElement>
                {
                    ["op"] = System.Text.Json.JsonSerializer.SerializeToElement("search"),
                    ["q"] = System.Text.Json.JsonSerializer.SerializeToElement("vague-criteria")
                });
            var json = System.Text.Json.JsonSerializer.Serialize(board);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var hits = doc.RootElement.GetProperty("hits");
            Assert.Contains(hits.EnumerateArray(), h => h.GetProperty("id").GetString() == "vague-criteria");

            var s = IdeQrhChannel.SuggestFor(Ctx(phase: "explore"));
            Assert.Equal("vague-criteria", s.HotId);
            Assert.Contains("intake-brief", s.RelatedIds);
        }
        finally
        {
            IdeSettingsStore.Unset(IdeQrhChannel.OverlayKey);
        }
    }

    [Fact]
    public void Builtins_cover_three_shelves()
    {
        var shelves = IdeQrhChannel.Builtins().Select(p => p.Shelf).Distinct().OrderBy(s => s).ToArray();
        Assert.Equal(["abnormal", "emergency", "systems"], shelves);
    }
}
