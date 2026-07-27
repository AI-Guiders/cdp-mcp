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
}
