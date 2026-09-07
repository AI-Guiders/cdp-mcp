using Xunit;

namespace CdpMcp.Tests;

/// <summary>Вежливый почтальон (Света 2026-09-07): письмо не перебивает генерацию —
/// busy-проверка по хвосту лога opencode, конверт ждёт «exiting loop».</summary>
public class CideWakePolitenessTests : IDisposable
{
    readonly string _logPath;

    public CideWakePolitenessTests()
    {
        _logPath = Path.Combine(Path.GetTempPath(), "cdp-wake-log-" + Guid.NewGuid().ToString("N")[..8] + ".log");
        CideWakeChannels.Opencode.LogPathOverrideForTests = _logPath;
    }

    public void Dispose()
    {
        CideWakeChannels.Opencode.LogPathOverrideForTests = null;
        try { File.Delete(_logPath); } catch { /* best-effort */ }
    }

    [Fact]
    public void LastSessionEvent_Takes_Last_Entry_For_Session()
    {
        var tail = string.Join('\n',
            "timestamp=2026-09-07T10:00:00Z level=INFO message=stream session.id=ses_other",
            "timestamp=2026-09-07T10:01:00Z level=INFO message=stream session.id=ses_mine step=0",
            "timestamp=2026-09-07T10:02:00Z level=INFO message=loop session.id=ses_mine step=1");

        var (ts, ev) = CideWakeChannels.Opencode.LastSessionEvent(tail, "ses_mine");

        Assert.NotNull(ts);
        Assert.Contains("10:02", ts);
        Assert.Contains("loop", ev);
    }

    [Fact]
    public void IsSessionBusy_True_When_Generation_Running()
    {
        File.WriteAllLines(_logPath, new[]
        {
            "timestamp=" + DateTimeOffset.UtcNow.AddMinutes(-1).ToString("yyyy-MM-ddTHH:mm:ss.fffZ") +
            " level=INFO message=stream session.id=ses_x modelID=deepseek agent=build",
        });

        Assert.True(CideWakeChannels.Opencode.IsSessionBusy("ses_x"));
    }

    [Fact]
    public void IsSessionBusy_False_After_Exiting_Loop()
    {
        File.WriteAllLines(_logPath, new[]
        {
            "timestamp=" + DateTimeOffset.UtcNow.AddMinutes(-1).ToString("yyyy-MM-ddTHH:mm:ss.fffZ") +
            " level=INFO message=stream session.id=ses_x",
            "timestamp=" + DateTimeOffset.UtcNow.AddMinutes(-1).ToString("yyyy-MM-ddTHH:mm:ss.fffZ") +
            " level=INFO message=\"exiting loop\" session.id=ses_x",
        });

        Assert.False(CideWakeChannels.Opencode.IsSessionBusy("ses_x"));
    }

    [Fact]
    public void IsSessionBusy_False_When_Stale_Busy_Over_Five_Minutes()
    {
        // застрявший busy не блокирует вечно: последняя запись старше 5 минут
        File.WriteAllLines(_logPath, new[]
        {
            "timestamp=" + DateTimeOffset.UtcNow.AddMinutes(-7).ToString("yyyy-MM-ddTHH:mm:ss.fffZ") +
            " level=INFO message=stream session.id=ses_x",
        });

        Assert.False(CideWakeChannels.Opencode.IsSessionBusy("ses_x"));
    }

    [Fact]
    public void IsSessionBusy_False_Without_Log_Or_Session_Entries()
    {
        // лога нет вовсе (файл не создан)
        Assert.False(CideWakeChannels.Opencode.IsSessionBusy("ses_absent"));

        // лог есть, но сессии в нём нет
        File.WriteAllLines(_logPath, new[]
        {
            "timestamp=" + DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") +
            " level=INFO message=stream session.id=ses_other",
        });
        Assert.False(CideWakeChannels.Opencode.IsSessionBusy("ses_absent"));
    }
}
