using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public class IdeRefactorPlanChannelTests
{
    static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    [Fact]
    public void Debt_ok_for_open_project_file()
    {
        var path = Path.Combine(RepoRoot, "IdeCockpit.cs");
        Assert.True(File.Exists(path), path);

        var store = new DocumentBufferStore();
        store.Open(path);
        var session = new SessionContext { ProjectRoot = RepoRoot };

        var result = IdeRefactorPlanChannel.Handle(store, session, new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("debt"),
            ["path"] = JsonSerializer.SerializeToElement(path),
            ["scope"] = JsonSerializer.SerializeToElement("file")
        });

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("debt", doc.RootElement.GetProperty("op").GetString());
        Assert.Equal(IdeRefactorPlanChannel.GoName, doc.RootElement.GetProperty("go").GetString());
    }

    [Fact]
    public void Partials_lists_siblings_for_stem()
    {
        var path = Path.Combine(RepoRoot, "IdeIgniteArmHost.cs");
        Assert.True(File.Exists(path), path);

        var store = new DocumentBufferStore();
        var session = new SessionContext { ProjectRoot = RepoRoot };

        var result = IdeRefactorPlanChannel.Handle(store, session, new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("partials"),
            ["path"] = JsonSerializer.SerializeToElement(path),
            ["topic"] = JsonSerializer.SerializeToElement("Decide")
        });

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("partials").GetProperty("siblings").GetArrayLength() >= 2);
        Assert.Contains(
            "IdeIgniteArmHost.Decide",
            doc.RootElement.GetProperty("partials").GetProperty("suggested_rel").GetString() ?? "",
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Recommend_Program_under_warn_is_leave_not_introduce()
    {
        var path = Path.Combine(RepoRoot, "Program.cs");
        Assert.True(File.Exists(path), path);

        var store = new DocumentBufferStore();
        store.Open(path);
        var session = new SessionContext { ProjectRoot = RepoRoot };

        var result = IdeRefactorPlanChannel.Handle(store, session, new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("recommend"),
            ["path"] = JsonSerializer.SerializeToElement(path),
            ["scope"] = JsonSerializer.SerializeToElement("file")
        });

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        var rec = doc.RootElement.GetProperty("recommend");
        Assert.Equal("leave", rec.GetProperty("verdict").GetString());
        Assert.Equal("top_level_statements", rec.GetProperty("shape").GetProperty("kind").GetString());
        Assert.Equal("none", rec.GetProperty("cut").GetProperty("kind").GetString());
    }

    [Fact]
    public void Recommend_Csproj_is_leave_non_csharp()
    {
        var path = Path.Combine(RepoRoot, "CdpMcp.csproj");
        Assert.True(File.Exists(path), path);

        var store = new DocumentBufferStore();
        store.Open(path);
        var session = new SessionContext { ProjectRoot = RepoRoot };

        var result = IdeRefactorPlanChannel.Handle(store, session, new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("recommend"),
            ["path"] = JsonSerializer.SerializeToElement(path),
            ["scope"] = JsonSerializer.SerializeToElement("file")
        });

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        var rec = doc.RootElement.GetProperty("recommend");
        Assert.Equal("leave", rec.GetProperty("verdict").GetString());
        Assert.Equal("non_csharp", rec.GetProperty("shape").GetProperty("kind").GetString());
        Assert.Equal("none", rec.GetProperty("cut").GetProperty("kind").GetString());
    }

    [Fact]
    public void Plan_includes_recommend_for_Build_partial()
    {
        var path = Path.Combine(RepoRoot, "IdeCockpit.Build.cs");
        Assert.True(File.Exists(path), path);

        var store = new DocumentBufferStore();
        store.Open(path);
        var session = new SessionContext { ProjectRoot = RepoRoot };

        var result = IdeRefactorPlanChannel.Handle(store, session, new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("plan"),
            ["path"] = JsonSerializer.SerializeToElement(path),
            ["scope"] = JsonSerializer.SerializeToElement("file")
        });

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(IdeRefactorPlanChannel.Schema, doc.RootElement.GetProperty("schema").GetString());
        Assert.True(doc.RootElement.TryGetProperty("recommend", out var rec));
        Assert.True(rec.GetProperty("ok").GetBoolean());
        var kind = rec.GetProperty("cut").GetProperty("kind").GetString();
        Assert.True(
            kind is "extract_method" or "peel_partial" or "leave" or "none",
            $"unexpected cut kind: {kind}");
    }
}
