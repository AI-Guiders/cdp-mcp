using Xunit;

namespace CdpMcp.Tests;

public sealed class IdeMfdSeatsFoldTests
{
    [Fact]
    public void Schema_is_cockpit_v1_20()
    {
        Assert.Equal("cockpit/v1.20", IdeCockpit.SchemaVersion);
    }

    [Theory]
    [InlineData("sys", "m")]
    [InlineData("chk", "m")]
    [InlineData("ecl", "m")]
    [InlineData("qrh", "m")]
    [InlineData("eqrh", "m")]
    [InlineData("review", "p")]
    [InlineData("gates", "p")]
    [InlineData("quality", "p")]
    [InlineData("problems", "p")]
    [InlineData("plugins", "p")]
    [InlineData("errlist", "p")]
    public void Soft_organ_seat_policy(string organ, string seat)
    {
        Assert.Equal(seat, IdeDeskSeats.ResolveSeatForOrgan(organ));
    }

    [Theory]
    [InlineData("sys", "sys")]
    [InlineData("chk", "ecl")]
    [InlineData("ecl", "ecl")]
    [InlineData("qrh", "qrh")]
    [InlineData("eqrh", "qrh")]
    [InlineData("gates", "gates")]
    [InlineData("problems", "problems")]
    [InlineData("err", "problems")]
    [InlineData("plugins", "plugins")]
    [InlineData("vsix", "plugins")]
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
    public void Ccl_bare_chk_sets_go_ecl()
    {
        var applied = IdeRepl.Apply("chk", new Dictionary<string, System.Text.Json.JsonElement>());
        Assert.NotNull(applied);
        Assert.Null(applied!.Value.Direct);
        Assert.True(applied.Value.Args.TryGetValue("go", out var go));
        Assert.Equal("ecl", go.GetString());
    }

    [Fact]
    public void Ccl_bare_ecl_sets_go()
    {
        var applied = IdeRepl.Apply("ecl", new Dictionary<string, System.Text.Json.JsonElement>());
        Assert.NotNull(applied);
        Assert.Null(applied!.Value.Direct);
        Assert.True(applied.Value.Args.TryGetValue("go", out var go));
        Assert.Equal("ecl", go.GetString());
    }

    [Fact]
    public void Ccl_bare_qrh_sets_go()
    {
        var applied = IdeRepl.Apply("qrh", new Dictionary<string, System.Text.Json.JsonElement>());
        Assert.NotNull(applied);
        Assert.Null(applied!.Value.Direct);
        Assert.True(applied.Value.Args.TryGetValue("go", out var go));
        Assert.Equal("qrh", go.GetString());
    }

    [Fact]
    public void Ccl_qrh_open_sets_args()
    {
        var applied = IdeRepl.Apply("qrh open dap-pdb-lock", new Dictionary<string, System.Text.Json.JsonElement>());
        Assert.NotNull(applied);
        Assert.Null(applied!.Value.Direct);
        Assert.True(applied.Value.Args.TryGetValue("go", out var go));
        Assert.Equal("qrh", go.GetString());
        Assert.True(applied.Value.Args.TryGetValue("go_args", out var ga));
        Assert.Equal("open", ga.GetProperty("op").GetString());
        Assert.Equal("dap-pdb-lock", ga.GetProperty("id").GetString());
    }

    [Fact]
    public void Ccl_eqrh_alias_sets_go_qrh()
    {
        var applied = IdeRepl.Apply("eqrh", new Dictionary<string, System.Text.Json.JsonElement>());
        Assert.NotNull(applied);
        Assert.Null(applied!.Value.Direct);
        Assert.True(applied.Value.Args.TryGetValue("go", out var go));
        Assert.Equal("qrh", go.GetString());
    }

    [Fact]
    public void Ccl_problems_row_sets_aim_args()
    {
        var applied = IdeRepl.Apply("problems 1", new Dictionary<string, System.Text.Json.JsonElement>());
        Assert.NotNull(applied);
        Assert.Null(applied!.Value.Direct);
        Assert.True(applied.Value.Args.TryGetValue("go", out var go));
        Assert.Equal("problems", go.GetString());
        Assert.True(applied.Value.Args.TryGetValue("go_args", out var ga));
        Assert.Equal("1", ga.GetProperty("row").GetString());
        Assert.True(ga.GetProperty("aim").GetBoolean());
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
