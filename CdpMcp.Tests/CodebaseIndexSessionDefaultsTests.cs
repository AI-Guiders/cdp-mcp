using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public sealed class CodebaseIndexSessionDefaultsTests
{
    [Fact]
    public void WithSession_injects_workspace_and_solution_when_missing()
    {
        var session = new SessionContext
        {
            ProjectRoot = @"D:\repo\cascade-ide",
            SolutionOrProjectPath = @"D:\repo\cascade-ide\CascadeIDE.sln",
            ScmRoot = @"D:\repo\cascade-ide",
        };
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["query"] = JsonSerializer.SerializeToElement("workspace.toml"),
        };

        var filled = CodebaseIndexSessionDefaults.WithSession(args, session);

        Assert.Equal(@"D:\repo\cascade-ide", filled["workspace_path"].GetString());
        Assert.Equal(@"D:\repo\cascade-ide\CascadeIDE.sln", filled["solution_path"].GetString());
        Assert.Equal("workspace.toml", filled["query"].GetString());
    }

    [Fact]
    public void WithSession_preserves_explicit_overrides()
    {
        var session = new SessionContext
        {
            ProjectRoot = @"D:\repo\cascade-ide",
            SolutionOrProjectPath = @"D:\repo\cascade-ide\CascadeIDE.sln",
        };
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["workspace_path"] = JsonSerializer.SerializeToElement(@"D:\other"),
            ["solution_path"] = JsonSerializer.SerializeToElement(@"D:\other\Other.sln"),
            ["query"] = JsonSerializer.SerializeToElement("x"),
        };

        var filled = CodebaseIndexSessionDefaults.WithSession(args, session);

        Assert.Equal(@"D:\other", filled["workspace_path"].GetString());
        Assert.Equal(@"D:\other\Other.sln", filled["solution_path"].GetString());
        Assert.Same(args, filled);
    }

    [Fact]
    public void OptionalSessionSchema_drops_workspace_path_from_required()
    {
        var schema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                workspace_path = new { type = "string", description = "Корень workspace." },
                solution_path = new { type = "string", description = "Optional scope." },
                query = new { type = "string" },
            },
            required = new[] { "workspace_path", "query" },
        });

        var patched = CodebaseIndexSessionDefaults.OptionalSessionSchema(schema);
        Assert.True(patched.TryGetProperty("required", out var req));
        var required = req.EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.DoesNotContain("workspace_path", required);
        Assert.Contains("query", required);

        Assert.Contains(
            "Optional after cdp_open",
            patched.GetProperty("properties").GetProperty("workspace_path").GetProperty("description").GetString(),
            StringComparison.Ordinal);
    }
}
