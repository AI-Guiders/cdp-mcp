#nullable enable
using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;
using CdpMcp.GraphQl;
using Xunit;

namespace CdpMcp.Tests;

public sealed class GraphQlKnowledgeHotRouteTests
{
    [Fact]
    public async Task HotContext_dispatches_memory_session_with_session_workspace()
    {
        string? tool = null;
        IReadOnlyDictionary<string, JsonElement>? captured = null;
        var call = new CdpGraphQlCall
        {
            Session = new SessionContext { ProjectRoot = "D:/ws/open", ScmRoot = "D:/ws/repo" },
            DocStore = new DocumentBufferStore(),
            Settings = new CdpSettings(),
            DispatchToolAsync = (name, args, _) =>
            {
                tool = name;
                captured = args;
                return Task.FromResult("""{"active_scope":"door-to-singularity","loaded_sections":[],"content":""}""");
            }
        };

        var q = new KnowledgeReadQuery(call);
        var node = await q.HotContext();

        Assert.Equal("memory_session_read_hot_context", tool);
        Assert.NotNull(captured);
        Assert.Equal("D:/ws/open", captured!["workspace_path"].GetString());
        Assert.Equal("knowledge_hot_context/v0", node.Schema);
        Assert.True(node.Ok);
    }

    [Fact]
    public async Task RouteContext_dispatches_with_query_and_clamps_limits()
    {
        string? tool = null;
        IReadOnlyDictionary<string, JsonElement>? captured = null;
        var call = new CdpGraphQlCall
        {
            Session = new SessionContext { ProjectRoot = "D:/ws" },
            DocStore = new DocumentBufferStore(),
            Settings = new CdpSettings(),
            DispatchToolAsync = (name, args, _) =>
            {
                tool = name;
                captured = args;
                return Task.FromResult("""{"sections":[],"content":""}""");
            }
        };

        var q = new KnowledgeReadQuery(call);
        _ = await q.RouteContext("index-knowledge-router", maxSections: 99, maxChars: 999);

        Assert.Equal("memory_session_route_context", tool);
        Assert.Equal("index-knowledge-router", captured!["query"].GetString());
        Assert.Equal(20, captured["max_sections"].GetInt32());
        Assert.Equal(1000, captured["max_chars"].GetInt32());
    }
}
