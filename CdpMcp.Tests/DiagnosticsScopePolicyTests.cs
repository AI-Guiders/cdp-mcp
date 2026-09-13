#nullable enable
using System.IO;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public sealed class DiagnosticsScopePolicyTests
{
    [Fact]
    public void Defaults_to_syntax_without_session_anchor()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-scope-" + Path.GetRandomFileName());
        var file = Path.Combine(root, "Foo.cs");
        var session = new SessionContext { ProjectRoot = root };

        Assert.Equal("syntax", DiagnosticsScopePolicy.ResolveDefault(session, file));
    }

    [Fact]
    public void Defaults_to_project_for_file_under_open_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-scope-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        var sln = Path.Combine(root, "App.slnx");
        File.WriteAllText(sln, "");
        var file = Path.Combine(root, "src", "Foo.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);

        var session = new SessionContext
        {
            ProjectRoot = root,
            SolutionOrProjectPath = sln,
        };

        Assert.Equal("project", DiagnosticsScopePolicy.ResolveDefault(session, file));
    }

    [Fact]
    public void Defaults_to_syntax_for_cdp_scratch()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-scope-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        var scratchDir = Path.Combine(root, ".cdp", "scratch");
        Directory.CreateDirectory(scratchDir);
        var file = Path.Combine(scratchDir, "untitled-1.cs");
        var session = new SessionContext
        {
            ProjectRoot = root,
            SolutionOrProjectPath = Path.Combine(root, "App.slnx"),
        };

        Assert.Equal("syntax", DiagnosticsScopePolicy.ResolveDefault(session, file));
    }

    [Fact]
    public void Explicit_scope_wins()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-scope-" + Path.GetRandomFileName());
        var file = Path.Combine(root, "Foo.cs");
        var session = new SessionContext
        {
            ProjectRoot = root,
            SolutionOrProjectPath = Path.Combine(root, "App.slnx"),
        };

        Assert.Equal("syntax", DiagnosticsScopePolicy.ResolveDefault(session, file, explicitScope: "syntax"));
    }

    [Fact]
    public void Defaults_to_project_when_file_path_omitted_and_session_open()
    {
        var session = new SessionContext
        {
            ProjectRoot = @"D:\repo",
            SolutionOrProjectPath = @"D:\repo\App.slnx",
        };

        Assert.Equal("project", DiagnosticsScopePolicy.ResolveDefault(session, filePath: null));
    }
}
