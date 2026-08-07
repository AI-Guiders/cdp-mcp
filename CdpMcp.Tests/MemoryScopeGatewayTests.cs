#nullable enable
using System.Text.Json;
using CdpMcp.Backends;
using Xunit;

namespace CdpMcp.Tests;

public sealed class MemoryScopeGatewayTests
{
    [Fact]
    public void World_dot_root_allows_knowledge_hub_file()
    {
        var gw = new MemoryScopeGateway("memory_world", ["worlds", "META", "."]);
        var args = gw.Apply(
            "read_knowledge_file",
            new Dictionary<string, JsonElement>
            {
                ["file_path"] = JsonSerializer.SerializeToElement("SHOWCASE.md")
            });
        Assert.Equal("SHOWCASE.md", args["file_path"].GetString());
    }

    [Fact]
    public void World_dot_root_refuses_non_hub_subdir()
    {
        var gw = new MemoryScopeGateway("memory_world", ["worlds", "META", "."]);
        var ex = Assert.Throws<ArgumentException>(() => gw.Apply(
            "read_knowledge_file",
            new Dictionary<string, JsonElement>
            {
                ["file_path"] = JsonSerializer.SerializeToElement("work/projects/x.md")
            }));
        Assert.Contains("outside memory_world roots", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_without_dot_still_refuses_hub_file()
    {
        var gw = new MemoryScopeGateway("memory_project", ["work/projects", "personal"]);
        var ex = Assert.Throws<ArgumentException>(() => gw.Apply(
            "read_knowledge_file",
            new Dictionary<string, JsonElement>
            {
                ["file_path"] = JsonSerializer.SerializeToElement("SHOWCASE.md")
            }));
        Assert.Contains("knowledge-root hub files need", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void List_inject_skips_dot_prefers_worlds()
    {
        var gw = new MemoryScopeGateway("memory_world", ["worlds", "META", "."]);
        var args = gw.Apply(
            "list_knowledge_files",
            new Dictionary<string, JsonElement>());
        Assert.Equal("worlds", args["subdir"].GetString());
    }

    [Fact]
    public void List_path_dot_maps_to_subdir_hub_no_worlds_inject()
    {
        var gw = new MemoryScopeGateway("memory_world", ["worlds", "META", "."]);
        var args = gw.Apply(
            "list_knowledge_files",
            new Dictionary<string, JsonElement>
            {
                ["path"] = JsonSerializer.SerializeToElement(".")
            });
        Assert.Equal(".", args["subdir"].GetString());
        Assert.Equal(".", args["path"].GetString());
    }

    [Fact]
    public void List_subdir_dot_keeps_hub_no_worlds_inject()
    {
        var gw = new MemoryScopeGateway("memory_world", ["worlds", "META", "."]);
        var args = gw.Apply(
            "list_knowledge_files",
            new Dictionary<string, JsonElement>
            {
                ["subdir"] = JsonSerializer.SerializeToElement(".")
            });
        Assert.Equal(".", args["subdir"].GetString());
    }
}
