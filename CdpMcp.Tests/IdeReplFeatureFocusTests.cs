using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public class IdeReplFeatureFocusTests
{
    static readonly Dictionary<string, JsonElement> Empty = new(StringComparer.Ordinal);

    [Fact]
    public void Feature_list_maps_to_board_not_junk_title()
    {
        var applied = IdeRepl.Apply("feature list", Empty);
        Assert.NotNull(applied);
        Assert.Null(applied.Value.Direct);
        Assert.True(applied.Value.Args.TryGetValue("tm_op", out var tm));
        Assert.Equal("board", tm.GetString());
        Assert.False(applied.Value.Args.TryGetValue("go_args", out _));
    }

    [Theory]
    [InlineData("feature done")]
    [InlineData("task focus")]
    [InlineData("task drop")]
    [InlineData("feature start")]
    public void Verb_as_title_is_rejected(string line)
    {
        var applied = IdeRepl.Apply(line, Empty);
        Assert.NotNull(applied);
        Assert.NotNull(applied.Value.Direct);
    }

    [Fact]
    public void Start_maps_to_tm_op_start()
    {
        var applied = IdeRepl.Apply("start", Empty);
        Assert.NotNull(applied);
        Assert.Null(applied.Value.Direct);
        Assert.True(applied.Value.Args.TryGetValue("tm_op", out var tm));
        Assert.Equal("start", tm.GetString());
    }

    [Fact]
    public void Shipped_maps_to_tm_op_shipped()
    {
        var applied = IdeRepl.Apply("shipped", Empty);
        Assert.NotNull(applied);
        Assert.Null(applied.Value.Direct);
        Assert.True(applied.Value.Args.TryGetValue("tm_op", out var tm));
        Assert.Equal("shipped", tm.GetString());
    }

    [Fact]
    public void FormatWallElapsed_formats_human()
    {
        var start = DateTimeOffset.Parse("2026-07-28T04:00:00Z");
        Assert.Equal("45s", IdeTaskManager.FormatWallElapsed(start, start.AddSeconds(45)));
        Assert.Equal("8m", IdeTaskManager.FormatWallElapsed(start, start.AddMinutes(8)));
        Assert.Equal("1h05m", IdeTaskManager.FormatWallElapsed(start, start.AddHours(1).AddMinutes(5)));
    }

    [Fact]
    public void Start_phase_maps_to_tm_op()
    {
        var applied = IdeRepl.Apply("start_phase act", Empty);
        Assert.NotNull(applied);
        Assert.True(applied.Value.Args.TryGetValue("tm_op", out var tm));
        Assert.Equal("start_phase", tm.GetString());
        Assert.True(applied.Value.Args.TryGetValue("go_args", out var ga));
        using var doc = JsonDocument.Parse(ga.GetRawText());
        Assert.Equal("act", doc.RootElement.GetProperty("phase").GetString());
    }

    [Fact]
    public void Complete_phase_maps_to_tm_op()
    {
        var applied = IdeRepl.Apply("complete_phase", Empty);
        Assert.NotNull(applied);
        Assert.True(applied.Value.Args.TryGetValue("tm_op", out var tm));
        Assert.Equal("complete_phase", tm.GetString());
    }

    [Fact]
    public void FormatPhaseSegments_keeps_reentry_visits_separate()
    {
        var t0 = DateTimeOffset.Parse("2026-07-28T04:00:00Z");
        var rows = new (string, string, DateTimeOffset)[]
        {
            ("phase.start", "act", t0),
            ("phase.complete", "act", t0.AddMinutes(2)),
            ("phase.start", "verify", t0.AddMinutes(2)),
            ("phase.complete", "verify", t0.AddMinutes(3)),
            ("phase.start", "act", t0.AddMinutes(3)),
        };
        var suffix = IdeTaskManager.FormatPhaseSegmentsSuffix(rows, t0.AddMinutes(5));
        Assert.Equal(" · act 2m · verify 1m · act …2m", suffix);
    }

    [Fact]
    public void FormatWallSuffix_frozen_completed_has_no_ellipsis()
    {
        var start = DateTimeOffset.Parse("2026-07-28T04:00:00Z");
        var done = start.AddMinutes(47);
        var frozen = IdeTaskManager.FormatWallClockSuffix(start, done, done);
        var open = IdeTaskManager.FormatWallClockSuffix(start, null, done);
        Assert.Equal(" · wall 47m", frozen);
        Assert.Equal(" · wall …47m", open);
    }

    [Fact]
    public void Feature_at_focus_strips_directive_from_title()
    {
        var applied = IdeRepl.Apply("feature night-refactor @focus", Empty);
        Assert.NotNull(applied);
        Assert.Null(applied.Value.Direct);

        Assert.True(applied.Value.Args.TryGetValue("go_args", out var ga));
        using var doc = JsonDocument.Parse(ga.GetRawText());
        Assert.Equal("night-refactor", doc.RootElement.GetProperty("title").GetString());
        Assert.Equal("feature", doc.RootElement.GetProperty("op").GetString());
    }

    [Theory]
    [InlineData("focus", "Y")]
    [InlineData("done", "ship")]
    [InlineData("park", "later")]
    public void SplitTitlePhase_strips_tm_directives(string directive, string name)
    {
        var (title, phase) = IdeRepl.SplitTitlePhase([name, "@" + directive]);
        Assert.Equal(name, title);
        Assert.Null(phase);
    }

    [Fact]
    public void SplitTitlePhase_keeps_phase_affinity()
    {
        var (title, phase) = IdeRepl.SplitTitlePhase(["omit-tiles", "@act"]);
        Assert.Equal("omit-tiles", title);
        Assert.Equal("act", phase);
    }
}
