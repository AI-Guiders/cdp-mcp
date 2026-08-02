#nullable enable
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public sealed class IdeLocalClockTests
{
    [Fact]
    public void DayPart_covers_hours()
    {
        Assert.Equal("morning", IdeLocalClock.DayPart(At(2026, 8, 2, 9)));
        Assert.Equal("day", IdeLocalClock.DayPart(At(2026, 8, 2, 14)));
        Assert.Equal("evening", IdeLocalClock.DayPart(At(2026, 8, 2, 19)));
        Assert.Equal("night", IdeLocalClock.DayPart(At(2026, 8, 2, 23)));
        Assert.Equal("night", IdeLocalClock.DayPart(At(2026, 8, 2, 3)));
    }

    [Fact]
    public void PulseLine_includes_local_date_and_daypart()
    {
        IdeLocalClock.NowOverride = () => At(2026, 8, 2, 10, 15);
        try
        {
            var line = IdeLocalClock.PulseLine();
            Assert.Contains("2026-08-02 10:15", line, StringComparison.Ordinal);
            Assert.Contains("morning", line, StringComparison.Ordinal);
            Assert.StartsWith("local ", line, StringComparison.Ordinal);
        }
        finally
        {
            IdeLocalClock.ResetForTests();
        }
    }

    [Fact]
    public void Deadlines_citizen_chain_days_left()
    {
        IdeLocalClock.NowOverride = () => At(2026, 8, 1, 12);
        try
        {
            dynamic card = IdeLocalClock.PulseCard();
            var deadlines = (object[])card.deadlines;
            Assert.Single(deadlines);
            dynamic d0 = deadlines[0];
            Assert.Equal("citizen_chain", (string)d0.id);
            Assert.Equal("2026-08-15", (string)d0.due);
            Assert.Equal(14, (int)d0.days_left);
            Assert.False((bool)d0.overdue);
        }
        finally
        {
            IdeLocalClock.ResetForTests();
        }
    }

    [Fact]
    public void MonthAscii_marks_today()
    {
        IdeLocalClock.NowOverride = () => At(2026, 8, 2, 12);
        try
        {
            var grid = IdeLocalClock.MonthAscii();
            Assert.Contains("август 2026", grid, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[ 2]", grid, StringComparison.Ordinal);
            Assert.Contains("пн вт ср", grid, StringComparison.Ordinal);
        }
        finally
        {
            IdeLocalClock.ResetForTests();
        }
    }

    [Fact]
    public void CalendarChannel_scene_ok()
    {
        IdeLocalClock.NowOverride = () => At(2026, 8, 2, 21, 5);
        try
        {
            dynamic pulse = IdeCalendarChannel.Handle(new SessionContext(), null);
            Assert.True((bool)pulse.ok);
            Assert.Equal("calendar", (string)pulse.go);
            Assert.Contains("evening", (string)pulse.pulse, StringComparison.Ordinal);
            Assert.Contains("[ 2]", (string)pulse.month, StringComparison.Ordinal);
        }
        finally
        {
            IdeLocalClock.ResetForTests();
        }
    }

    static DateTimeOffset At(int y, int m, int d, int h, int min = 0) =>
        new(y, m, d, h, min, 0, TimeSpan.FromHours(3));
}
