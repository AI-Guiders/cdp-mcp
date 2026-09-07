using Cdp.ScriptableIde;
using Xunit;

namespace CdpMcp.Tests;

/// <summary>
/// forum 003 AddRelated wave: workspace-family doc resolution (own + sibling),
/// comment-preserving toml merge, honest rejections with candidates (FTC case).
/// </summary>
public sealed class CorrespondenceAddRelatedTests : IDisposable
{
    readonly string _baseDir;
    readonly string _ws;
    readonly string _sibling;
    readonly string _codeFile;

    public CorrespondenceAddRelatedTests()
    {
        _baseDir = Path.Combine(Path.GetTempPath(), "corr-addrelated-" + Guid.NewGuid().ToString("N"));
        _sibling = Path.Combine(_baseDir, "sibling-docs");
        _ws = Path.Combine(_baseDir, "ws-repo");
        var src = Path.Combine(_ws, "src", "Cdp.ScriptableIde");
        Directory.CreateDirectory(src);
        Directory.CreateDirectory(_sibling);
        Directory.CreateDirectory(Path.Combine(_ws, ".cascade"));

        Directory.CreateDirectory(Path.Combine(_sibling, "docs", "math"));
        File.WriteAllText(Path.Combine(_sibling, "docs", "math", "03-frozen.md"), "# 03 frozen\n");
        File.WriteAllText(Path.Combine(_sibling, "docs", "math", "04-jobs.md"), "# 04 jobs\n");

        _codeFile = Path.Combine(src, "WorktreePlanRunner.cs");
        File.WriteAllText(_codeFile, "class WorktreePlanRunner { }\n");

        File.WriteAllText(
            Path.Combine(_ws, ".cascade", "workspace.toml"),
            """
            # HEADER COMMENT — keep me
            [workspace.adr]
            auto_include = "none"
            max_related = 8

            [workspace.adr.map]
            # map comment
            "src/Cdp.ScriptableIde/WorktreePlanRunner.cs" = [
              "sibling-docs/docs/math/03-frozen.md",
            ]
            """);
    }

    [Fact]
    public void AddRelated_NewKey_AppendsBlock_PreservesComments()
    {
        var r = WorkspaceCorrespondence.AddRelated(
            _ws, "src/Cdp.ScriptableIde/NewFile.cs", "sibling-docs/docs/math/03-frozen.md");

        Assert.True(r.Ok);
        Assert.True(r.Changed);
        Assert.Equal("sibling", r.DocKind);
        Assert.NotNull(r.DocAbs);
        var text = File.ReadAllText(r.TomlPath);
        Assert.Contains("# HEADER COMMENT", text);
        Assert.Contains("# map comment", text);
        Assert.Contains("\"src/Cdp.ScriptableIde/NewFile.cs\" = [", text);
        Assert.Contains("\"sibling-docs/docs/math/03-frozen.md\",", text);
    }

    [Fact]
    public void AddRelated_ExistingKey_Appends_ThenDedupes()
    {
        var first = WorkspaceCorrespondence.AddRelated(
            _ws, "src/Cdp.ScriptableIde/WorktreePlanRunner.cs", "sibling-docs/docs/math/04-jobs.md");
        Assert.True(first.Ok);
        Assert.True(first.Changed);
        var afterFirst = File.ReadAllText(first.TomlPath);
        Assert.Contains("03-frozen.md", afterFirst);
        Assert.Contains("04-jobs.md", afterFirst);
        Assert.Contains("# map comment", afterFirst);

        var repeat = WorkspaceCorrespondence.AddRelated(
            _ws, "src/Cdp.ScriptableIde/WorktreePlanRunner.cs", "sibling-docs/docs/math/04-jobs.md");
        Assert.True(repeat.Ok);
        Assert.False(repeat.Changed);
    }

    [Fact]
    public void AddRelated_DocNotFound_RejectsWithCandidates()
    {
        var r = WorkspaceCorrespondence.AddRelated(
            _ws, "src/Cdp.ScriptableIde/NewFile.cs", "nowhere/docs/x.md");

        Assert.False(r.Ok);
        Assert.Equal("doc_not_found", r.Error);
        Assert.NotEmpty(r.Candidates);
        Assert.Contains(r.Candidates, c => c.Contains("nowhere"));
    }

    [Fact]
    public void AddRelated_PhysicalDoc_NormalizesToSiblingLogical()
    {
        var physical = Path.Combine(_sibling, "docs", "math", "04-jobs.md");
        var r = WorkspaceCorrespondence.AddRelated(
            _ws, "src/Cdp.ScriptableIde/NewFile.cs", physical);

        Assert.True(r.Ok);
        Assert.Equal("sibling-docs/docs/math/04-jobs.md", r.Doc);
        Assert.Equal("sibling", r.DocKind);
    }

    [Fact]
    public void Resolver_BareFilenameKey_Matches_AndSiblingDocCarriesAbs()
    {
        File.WriteAllText(
            Path.Combine(_ws, ".cascade", "workspace.toml"),
            """
            [workspace.adr]
            auto_include = "none"

            [workspace.adr.map]
            "WorktreePlanRunner.cs" = [
              "sibling-docs/docs/math/03-frozen.md",
            ]
            """);

        var result = WorkspaceCorrespondence.TryResolve(_codeFile, _ws);

        Assert.NotNull(result);
        var doc = Assert.Single(result.ForwardDocs);
        Assert.Equal("sibling-docs/docs/math/03-frozen.md", doc.Path);
        Assert.Equal("sibling", doc.Kind);
        Assert.NotNull(doc.Abs);
        Assert.Contains(Path.Combine("sibling-docs", "docs", "math", "03-frozen.md"), doc.Abs);
    }

    public void Dispose()
    {
        try { Directory.Delete(_baseDir, recursive: true); }
        catch { /* temp cleanup best-effort */ }
    }
}
