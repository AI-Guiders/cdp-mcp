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
        Assert.Contains("F:Program.cs", BracketLocate.Format(span));
    }

    [Fact]
    public void Parse_navigation_nested_anchor()
    {
        var span = BracketLocate.Parse("[Family:navigation;Command:open;Anchor:[F:README.md;L:10]]");
        Assert.Equal(BracketLocate.AxisFamily.Navigation, BracketLocate.ClassifyFamily(span, out _));
        Assert.NotNull(span.NestedAnchor);
        Assert.Equal("README.md", span.NestedAnchor!.File);
        Assert.Equal(10, span.NestedAnchor.LineStart);
    }

    [Fact]
    public void Parse_type_key_passthrough()
    {
        var span = BracketLocate.Parse("[F:Types.cs;T:MyNamespace.MyType;M:Run]");
        Assert.Equal("MyNamespace.MyType", span.TypeKey);
        Assert.Equal(BracketLocate.AxisFamily.Csharp, BracketLocate.ClassifyFamily(span, out _));
    }

    [Fact]
    public void Format_preferCanonical_emits_kind_code_edit()
    {
        var span = BracketLocate.Parse("[F:Program.cs;M:Foo;L:10]");
        var wire = BracketLocate.Format(span, preferCanonical: true);
        Assert.Equal("[Kind:CodeEdit; File:Program.cs; Member:Foo]", wire);
    }
}
