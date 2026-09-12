#nullable enable
using System.Text.Json;
using AgentNotes.Core;
using Xunit;

namespace CdpMcp.Tests;

/// <summary>CDP-ADR-0210 smoke against live agent-notes primary (when configured).</summary>
public sealed class KnowledgeCorpusRecallSmokeTests
{
    const string ConfigPath = @"D:/agent-notes-mcp/agent-notes-mcp.toml";

    static bool TryInit(out NotesStorage storage)
    {
        storage = new NotesStorage();
        if (!File.Exists(ConfigPath))
            return false;
        var code = AgentNotesBootstrap.TryLoadSettings(["--config", ConfigPath], out var settings, out _);
        if (code != 0 || settings is null)
            return false;
        AgentNotesRuntime.Initialize(settings, AgentNotesBootstrap.LoadedConfigPath);
        return true;
    }

    [Fact]
    public void Smoke_ai_incidents_federated_lookup()
    {
        if (!TryInit(out var storage))
            return;

        var json = storage.QueryKnowledgeTags(null, query: "ai-incidents");
        Assert.Contains("personal/ai-incidents/README.md", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Smoke_pml_resolve_engineering_pml()
    {
        if (!TryInit(out var storage))
            return;

        var json = storage.QueryKnowledgeTags(null, query: "PML", mode: "resolve");
        using var doc = JsonDocument.Parse(json);
        var tag = doc.RootElement.GetProperty("resolved_tag").GetString();
        Assert.Equal("#engineering-pml", tag);
        Assert.True(doc.RootElement.GetProperty("known").GetBoolean());
    }

    [Fact]
    public void Smoke_scope_first_ranks_cascade_ide()
    {
        if (!TryInit(out var storage))
            return;

        var json = storage.QueryKnowledgeTags(
            null,
            query: "cdp",
            mode: "lookup",
            activeScope: "door-to-singularity",
            limit: 15);
        using var doc = JsonDocument.Parse(json);
        var hits = doc.RootElement.GetProperty("hits");
        Assert.True(hits.GetArrayLength() > 0);
        var first = hits[0].GetProperty("path").GetString() ?? "";
        Assert.Contains("door-to-singularity", first, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Smoke_hot_search_without_workspace()
    {
        if (!TryInit(out var storage))
            return;

        var json = storage.Search("", "agent-notes", 5);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("query", out _));
    }
}
