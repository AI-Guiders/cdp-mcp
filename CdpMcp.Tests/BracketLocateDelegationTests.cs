using Cdp.ScriptableIde;
using Xunit;

namespace CdpMcp.Tests;

/// <summary>
/// CDP host smoke: BracketLocate is a thin façade over federation BracketReader (GUIDERS-ADR-0026 Wave 2).
/// </summary>
public sealed class BracketLocateDelegationTests
{
    [Fact]
    public void Parse_classify_format_code_wire_roundtrip()
    {
        var span = BracketLocate.Parse("[F:Program.cs;M:Foo;L:10]");
        Assert.Equal("Program.cs", span.File);
        Assert.Equal("Foo", span.MemberKey);
        Assert.Equal(10, span.LineStart);
        Assert.Equal(BracketLocate.AxisFamily.Csharp, BracketLocate.ClassifyFamily(span, out var error));
        Assert.Null(error);
        Assert.StartsWith("[Kind:CodeEdit;", BracketLocate.Format(span));
        Assert.Contains("Member:Foo", BracketLocate.Format(span));
    }

    [Fact]
    public void Parse_legacy_fml_wire_via_relation_boundary()
    {
        var span = BracketLocate.Parse("[F:Legacy.cs;M:Old;L:5]");
        Assert.Equal("Legacy.cs", span.File);
        Assert.Equal("Old", span.MemberKey);
        Assert.Equal(5, span.LineStart);
    }

    [Fact]
    public void Parse_rejects_legacy_family_navigation()
    {
        Assert.Throws<ArgumentException>(() =>
            BracketLocate.Parse("[Family:navigation;Command:open;Anchor:[F:README.md;L:10]]"));
    }

    [Fact]
    public void Parse_kind_nav_open_roundtrip()
    {
        var span = BracketLocate.Parse("[Kind:Nav; File:README.md; Line:10; Command:open]");
        Assert.Equal(BracketLocate.AxisFamily.Navigation, BracketLocate.ClassifyFamily(span, out _));
        Assert.Equal("README.md", span.File);
        Assert.Equal(10, span.LineStart);
        Assert.Equal("open", span.Command);
        Assert.Equal("[Kind:Nav; File:README.md; Line:10; Command:open]", BracketLocate.Format(span));
    }

    [Fact]
    public void Parse_kind_nav_member_roundtrip()
    {
        var span = BracketLocate.Parse("[Kind:Nav; File:README.md; Line:10; Member:Foo; Command:open]");
        Assert.Equal("README.md", span.File);
        Assert.Equal("Foo", span.MemberKey);
        Assert.Equal("[Kind:Nav; File:README.md; Line:10; Member:Foo; Command:open]", BracketLocate.Format(span));
    }

    [Fact]
    public void Format_command_only_navigation_emits_kind_nav()
    {
        var span = BracketLocate.Parse("[Kind:Nav; Command:restore]");
        Assert.Equal(BracketLocate.AxisFamily.Navigation, BracketLocate.ClassifyFamily(span, out _));
        Assert.Equal("[Kind:Nav; Command:restore]", BracketLocate.Format(span));
    }

    [Fact]
    public void Parse_kind_nav_wire()
    {
        var span = BracketLocate.Parse("[Kind:Nav; File:README.md; Line:10; Command:open]");
        Assert.Equal(BracketLocate.AxisFamily.Navigation, BracketLocate.ClassifyFamily(span, out _));
        Assert.Equal("README.md", span.File);
        Assert.Equal(10, span.LineStart);
        Assert.Equal("open", span.Command);
    }

    [Fact]
    public void Parse_type_key_passthrough()
    {
        var span = BracketLocate.Parse("[F:Types.cs;T:MyNamespace.MyType;M:Run]");
        Assert.Equal("MyNamespace.MyType", span.TypeKey);
        Assert.Equal(BracketLocate.AxisFamily.Csharp, BracketLocate.ClassifyFamily(span, out _));
    }

    [Fact]
    public void Format_default_emits_kind_code_edit()
    {
        var span = BracketLocate.Parse("[F:Program.cs;M:Foo;L:10]");
        var wire = BracketLocate.Format(span);
        Assert.Equal("[Kind:CodeEdit; File:Program.cs; Member:Foo]", wire);
    }
}
