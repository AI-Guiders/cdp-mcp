using Xunit;
using Cdp.ScriptableIde;

namespace CdpMcp.Tests;

public sealed partial class DocumentEditPlaneAnchorPlaceTests
{
    const string FsharpRecordBody =
        """
        module Fixture

        type FilterDefinition =
            { Kind: string
              Name: string }
        """;

    [Fact]
    public async Task Place_into_on_fsharp_record_inserts_inside_braces()
    {
        await using var fx = await AnchorFixture.CreateAsync(FsharpRecordBody, fileName: "Fixture.fs");
        var json = await fx.EditAnchorAsync(
            place: "into",
            text: "\n      ApplyMode: string",
            anchor: "[Kind:CodeEdit; File:Fixture.fs; Member:FilterDefinition]");

        Assert.Contains("\"place\": \"into\"", json, StringComparison.Ordinal);
        Assert.Contains("block_body_start", json, StringComparison.Ordinal);
        var open = fx.Text.IndexOf('{');
        var field = fx.Text.IndexOf("ApplyMode", StringComparison.Ordinal);
        var close = fx.Text.IndexOf('}');
        Assert.True(open >= 0 && field > open && close > field, fx.Text);
    }

    [Fact]
    public async Task Place_end_on_fsharp_record_inserts_before_closing_brace()
    {
        await using var fx = await AnchorFixture.CreateAsync(FsharpRecordBody, fileName: "Fixture.fs");
        var json = await fx.EditAnchorAsync(
            place: "end",
            text: "\n      ApplyMode: string",
            anchor: "[Kind:CodeEdit; File:Fixture.fs; Member:FilterDefinition]");

        Assert.Contains("\"place\": \"end\"", json, StringComparison.Ordinal);
        Assert.Contains("block_body_end", json, StringComparison.Ordinal);
        var name = fx.Text.IndexOf("Name: string", StringComparison.Ordinal);
        var field = fx.Text.IndexOf("ApplyMode", StringComparison.Ordinal);
        var close = fx.Text.IndexOf('}');
        Assert.True(name >= 0 && field > name && close > field, fx.Text);
    }

    [Fact]
    public async Task Place_into_on_braceless_fsharp_let_refuses_honestly()
    {
        const string body =
            """
            module Fixture

            let add a b =
                a + b
            """;
        await using var fx = await AnchorFixture.CreateAsync(body, fileName: "Fixture.fs");
        var before = fx.Text;
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            fx.EditAnchorAsync(
                place: "into",
                text: "\n    ()",
                anchor: "[Kind:CodeEdit; File:Fixture.fs; Member:add]"));

        Assert.Contains("place=into", ex.Message, StringComparison.Ordinal);
        Assert.Equal(before, fx.Text);
    }

    [Theory]
    [InlineData("type R =\n    { A: string // }\n      B: string }\n", true)]
    [InlineData("type R =\n    { A: string (* } *)\n      B: string }\n", true)]
    [InlineData("type R =\n    { A: string\n      B: string }\n", true)]
    [InlineData("let x = { A = \"}\" }\n", false)]
    [InlineData("let add a b =\n    a + b\n", false)]
    public void Fsharp_brace_interior_resolves_outermost_block(string body, bool expected)
    {
        var lines = body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var locus = new BracketSyntaxResolve.TextRange(1, 1, lines.Length, Math.Max(1, lines[^1].Length + 1));
        var ok = FSharpAnchorResolve.TryGetBraceInterior(body, locus, before: true, out _, out _);
        Assert.Equal(expected, ok);
    }

    [Fact]
    public void Fsharp_brace_interior_points_are_zero_width()
    {
        const string body =
            """
            type R =
                { A: string
                  B: string }
            """;
        var locus = new BracketSyntaxResolve.TextRange(1, 1, 3, 1);
        Assert.True(FSharpAnchorResolve.TryGetBraceInterior(body, locus, before: true, out var start, out var sd));
        Assert.Equal("block_body_start", sd);
        Assert.Equal(start.LineStart, start.LineEnd);
        Assert.Equal(start.ColumnStart, start.ColumnEnd);

        Assert.True(FSharpAnchorResolve.TryGetBraceInterior(body, locus, before: false, out var end, out var ed));
        Assert.Equal("block_body_end", ed);
        Assert.Equal(end.LineStart, end.LineEnd);
        Assert.Equal(end.ColumnStart, end.ColumnEnd);
        Assert.True(end.LineStart >= start.LineStart, $"start={start.LineStart} end={end.LineStart}");
    }

    [Fact]
    public async Task Place_replace_with_line_and_text_narrows_to_needle_line()
    {
        // Regression 2026-11-09: a member-less F# anchor used to fall back to the
        // whole-file locus, silently dropping the T axis — place=replace overwrote
        // the entire file. L+T must narrow to the needle span instead.
        await using var fx = await AnchorFixture.CreateAsync(FsharpRecordBody, fileName: "Fixture.fs");
        var json = await fx.EditAnchorAsync(
            place: "replace",
            text: "Name: string?",
            anchor: "[Kind:CodeEdit; File:Fixture.fs; Line:5; Text:Name: string]");

        Assert.Contains("fsharp:lines+T", json, StringComparison.Ordinal);
        Assert.Contains("Name: string?", fx.Text, StringComparison.Ordinal);
        Assert.Contains("Kind: string", fx.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Place_replace_with_text_only_narrows_to_needle_line()
    {
        await using var fx = await AnchorFixture.CreateAsync(FsharpRecordBody, fileName: "Fixture.fs");
        var json = await fx.EditAnchorAsync(
            place: "replace",
            text: "Kind: int",
            anchor: "[Kind:CodeEdit; File:Fixture.fs; Text:Kind: string]");

        Assert.Contains("fsharp:file+T", json, StringComparison.Ordinal);
        Assert.Contains("Kind: int", fx.Text, StringComparison.Ordinal);
        Assert.Contains("Name: string", fx.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Place_replace_on_whole_file_locus_refuses_instead_of_nuking()
    {
        await using var fx = await AnchorFixture.CreateAsync(FsharpRecordBody, fileName: "Fixture.fs");
        var before = fx.Text;
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            fx.EditAnchorAsync(
                place: "replace",
                text: "nuked",
                anchor: "[Kind:CodeEdit; File:Fixture.fs; Type:FilterDefinition]"));

        Assert.Contains("whole-file locus", ex.Message, StringComparison.Ordinal);
        Assert.Equal(before, fx.Text);
    }
}
