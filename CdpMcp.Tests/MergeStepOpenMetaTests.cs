using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public sealed class MergeStepOpenMetaTests
{
    [Fact]
    public void MergeStepOpenMeta_attaches_open_object()
    {
        const string step = """
            {"ok":true,"kind":"projects.create","summary":"created","data":{"project":"X.csproj"}}
            """;
        const string open = """
            {"root":"C:\\tmp\\x","scm_risk":"ancestor","scm_note":"parent scm","buffers_parked":1,"buffer_note":"parked"}
            """;

        var merged = IdeLanguageTools.MergeStepOpenMeta(step, open);
        using var doc = JsonDocument.Parse(merged);
        Assert.True(doc.RootElement.TryGetProperty("open", out var openEl));
        Assert.Equal("ancestor", openEl.GetProperty("scm_risk").GetString());
        Assert.Equal("parent scm", openEl.GetProperty("scm_note").GetString());
        Assert.Equal(1, openEl.GetProperty("buffers_parked").GetInt32());
        Assert.Equal("X.csproj", doc.RootElement.GetProperty("data").GetProperty("project").GetString());
    }

    [Fact]
    public void MergeStepOpenMeta_null_open_is_identity()
    {
        const string step = """{"ok":true}""";
        Assert.Equal(step, IdeLanguageTools.MergeStepOpenMeta(step, null));
        Assert.Equal(step, IdeLanguageTools.MergeStepOpenMeta(step, ""));
    }
}
