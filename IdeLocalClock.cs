#nullable enable
using System.Globalization;
using System.Text;

namespace CdpMcp;

/// <summary>
/// Machine-local wall clock for cockpit/plan (not UTC-only task wall).
/// Daypart + optional epic deadlines so agents see shift/date without guessing.
/// </summary>
internal static class IdeLocalClock
{
    public const string SchemaVersion = "local_clock/v0";

    /// <summary>Tests: freeze local now.</summary>
    internal static Func<DateTimeOffset>? NowOverride;

    public static DateTimeOffset Now => NowOverride?.Invoke() ?? DateTimeOffset.Now;

    public static void ResetForTests() => NowOverride = null;

    public static string DayPart(DateTimeOffset local) => local.Hour switch
    {
        >= 5 and < 12 => "morning",
        >= 12 and < 17 => "day",
        >= 17 and < 22 => "evening",
        _ => "night"
    };

    public static string PulseLine(DateTimeOffset? at = null)
    {
        var n = at ?? Now;
        var ru = CultureInfo.GetCultureInfo("ru-RU");
        return $"local {n.ToString("ddd", ru)} {n:yyyy-MM-dd HH:mm} {DayPart(n)} · UTC{n:zzz}";
    }

    public static object PulseCard(DateTimeOffset? at = null)
    {
        var n = at ?? Now;
        var ru = CultureInfo.GetCultureInfo("ru-RU");
        return new
        {
            schema = SchemaVersion,
            ok = true,
            pulse = PulseLine(n),
            local = n.ToString("yyyy-MM-dd HH:mm"),
            date = n.ToString("yyyy-MM-dd"),
            weekday = n.ToString("dddd", ru),
            weekday_en = n.ToString("dddd", CultureInfo.InvariantCulture),
            time = n.ToString("HH:mm"),
            tz = n.ToString("zzz"),
            daypart = DayPart(n),
            utc = n.ToUniversalTime().ToString("O"),
            deadlines = Deadlines(n)
        };
    }

    public static object[] Deadlines(DateTimeOffset local)
    {
        // Continuity contract frame (2026): full chain ≥1 citizen by 15.08.
        var due = new DateTimeOffset(2026, 8, 15, 23, 59, 59, local.Offset);
        var days = (due.Date - local.Date).Days;
        return
        [
            new
            {
                id = "citizen_chain",
                label = "≥1 citizen full chain",
                due = "2026-08-15",
                days_left = days,
                overdue = days < 0
            }
        ];
    }

    /// <summary>Month grid (Mon-first), ASCII for go=calendar [A].</summary>
    public static string MonthAscii(DateTimeOffset? at = null)
    {
        var n = at ?? Now;
        var first = new DateTimeOffset(n.Year, n.Month, 1, 0, 0, 0, n.Offset);
        var daysInMonth = DateTime.DaysInMonth(n.Year, n.Month);
        // Monday=0 … Sunday=6
        var start = ((int)first.DayOfWeek + 6) % 7;
        var sb = new StringBuilder();
        sb.Append(n.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("ru-RU"))).Append('\n');
        sb.Append("пн вт ср чт пт сб вс\n");
        for (var i = 0; i < start; i++)
            sb.Append("   ");
        for (var d = 1; d <= daysInMonth; d++)
        {
            // 4-wide cells so today [dd] aligns with " dd "
            sb.Append(d == n.Day ? $"[{d,2}]" : $" {d,2} ");
            if ((start + d) % 7 == 0)
                sb.Append('\n');
        }
        if ((start + daysInMonth) % 7 != 0)
            sb.Append('\n');
        return sb.ToString().TrimEnd();
    }
}
