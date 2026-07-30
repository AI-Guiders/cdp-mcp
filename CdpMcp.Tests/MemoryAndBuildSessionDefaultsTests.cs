using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public sealed class MemoryAndBuildSessionDefaultsTests
{
    [Fact]
    public void Memory_WithWorkspace_injects_project_root()
    {
        var session = new SessionContext
        {
            ProjectRoot = @"D:\repo\app",
            ScmRoot = @"D:\repo",
        };
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["query"] = JsonSerializer.SerializeToElement("continuity"),
        };

        var filled = MemorySessionDefaults.WithWorkspace(args, session);

        Assert.Equal(@"D:\repo\app", filled["workspace_path"].GetString());
    }

    [Fact]
    public void Memory_OptionalWorkspaceSchema_drops_required()
    {
        var schema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new { workspace_path = new { type = "string", description = "Каталог." }, query = new { type = "string" } },
            required = new[] { "workspace_path", "query" },
        });

        var patched = MemorySessionDefaults.OptionalWorkspaceSchema(schema);
        var required = patched.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.DoesNotContain("workspace_path", required);
        Assert.Contains("query", required);
        Assert.Contains("Optional after cdp_open", patched.GetProperty("properties").GetProperty("workspace_path").GetProperty("description").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WithSession_injects_solution_path()
    {
        var session = new SessionContext
        {
            ProjectRoot = @"D:\repo\app",
            SolutionOrProjectPath = @"D:\repo\app\App.sln",
        };
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        var filled = BuildSessionDefaults.WithSession(args, session);

        Assert.Equal(@"D:\repo\app\App.sln", filled["solution_path"].GetString());
    }
}
