using Xunit;

namespace CdpMcp.Tests;

public sealed class IdeMfdSeatsFoldTests
{
    [Fact]
    public void Schema_is_cockpit_v1_16()
    {
        Assert.Equal("cockpit/v1.16", IdeCockpit.SchemaVersion);
    }

    [Theory]
    [InlineData("sys", "m")]
    [InlineData("chk", "m")]
    [InlineData("gates", "p")]
    [InlineData("quality", "p")]
    public void Soft_organ_seat_policy(string organ, string seat)
    {
        Assert.Equal(seat, IdeDeskSeats.ResolveSeatForOrgan(organ));
    }

    [Theory]
    [InlineData("sys", "sys")]
    [InlineData("chk", "chk")]
    [InlineData("gates", "gates")]
    public void ShortOrgan_labels(string organ, string label)
    {
        Assert.Equal(label, IdeDeskView.ShortOrgan(organ));
    }

    [Fact]
    public void Ccl_bare_sys_sets_go()
    {
        var applied = IdeRepl.Apply("sys", new Dictionary<string, System.Text.Json.JsonElement>());
        Assert.NotNull(applied);
        Assert.Null(applied!.Value.Direct);
        Assert.True(applied.Value.Args.TryGetValue("go", out var go));
        Assert.Equal("sys", go.GetString());
    }

    [Fact]
    public void Ccl_bare_chk_sets_go()
    {
        var applied = IdeRepl.Apply("chk", new Dictionary<string, System.Text.Json.JsonElement>());
        Assert.NotNull(applied);
        Assert.Null(applied!.Value.Direct);
        Assert.True(applied.Value.Args.TryGetValue("go", out var go));
        Assert.Equal("chk", go.GetString());
    }

    [Fact]
    public void Ccl_nav_sets_go_nav()
    {
        var applied = IdeRepl.Apply("nav", new Dictionary<string, System.Text.Json.JsonElement>());
        Assert.NotNull(applied);
        Assert.True(applied!.Value.Args.TryGetValue("go", out var go));
        Assert.Equal("nav", go.GetString());
    }
}
