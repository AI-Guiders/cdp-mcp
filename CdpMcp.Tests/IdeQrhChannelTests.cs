using Xunit;

namespace CdpMcp.Tests;

public sealed class IdeQrhChannelTests
{
    static IdeChkChannel.ProbeCtx Ctx(
        string phase = "act",
        string? intent = null,
        bool gitDirty = false,
        bool dapStopped = false) =>
        new(
            ProjectOpen: true,
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
    public void Builtins_cover_three_shelves()
    {
        var shelves = IdeQrhChannel.Builtins().Select(p => p.Shelf).Distinct().OrderBy(s => s).ToArray();
        Assert.Equal(["abnormal", "emergency", "systems"], shelves);
    }
}
