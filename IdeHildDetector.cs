#nullable enable

namespace CdpMcp;

/// <summary>
/// Human-in-the-loop detector (pure FSM).
/// Voice/empty Composer + no text for <see cref="DefaultIdle"/> → edge <c>human_away</c> <b>once</b>.
/// Latch holds until human Composer text (not AutoI wake charge) — no re-fire after agent Stop→Voice.
/// Purpose: one AutoI wake → autonomous flight (not a 5s thrash loop).
/// </summary>
internal sealed class IdeHildDetector
{
    public static readonly TimeSpan DefaultIdle = TimeSpan.FromSeconds(5);

    public enum Status
    {
        Idle,
        Watching,
        HumanPresent,
        HumanAway
    }

    public readonly record struct Sample(
        string ButtonKind,
        string? InputText,
        DateTimeOffset Now,
        TimeSpan? Idle = null);

    public readonly record struct TickResult(Status Status, bool EdgeHumanAway, TimeSpan? QuietFor);

    Status _status = Status.Idle;
    DateTimeOffset? _quietSince;
    string _lastText = "";
    /// <summary>True after edge until human types — blocks plateau thrash.</summary>
    bool _awayLatched;

    public Status Current => _status;
    public bool AwayLatched => _awayLatched;
    public DateTimeOffset? QuietSince => _quietSince;

    public void ResetSpell()
    {
        _status = Status.Idle;
        _quietSince = null;
        _lastText = "";
        // Keep _awayLatched across agent Stop/Queue.
    }

    public void Reset()
    {
        ResetSpell();
        _awayLatched = false;
    }

    public TickResult Tick(Sample sample)
    {
        var idle = sample.Idle ?? DefaultIdle;
        if (idle <= TimeSpan.Zero)
            idle = DefaultIdle;

        var kind = NormalizeKind(sample.ButtonKind);
        var text = NormalizeText(sample.InputText);

        // Agent flying — end watch clocks; keep away latch.
        if (kind is "stop" or "queue")
        {
            ResetSpell();
            return new TickResult(Status.Idle, false, null);
        }

        // Human typed — clear latch. AutoI wake charge / bare Send do not count as return.
        if (text.Length > 0 && !IdeIgniteChannel.LooksLikeAutoIgnitionCharge(text))
        {
            _lastText = text;
            _quietSince = sample.Now;
            _awayLatched = false;
            _status = Status.HumanPresent;
            return new TickResult(Status.HumanPresent, false, TimeSpan.Zero);
        }

        // Machine charge in the box — ignore as human presence; keep latch clocks.
        if (IdeIgniteChannel.LooksLikeAutoIgnitionCharge(text))
            text = "";

        _lastText = text;

        // Already woke for this absence — stay latched, no second shot.
        if (_awayLatched)
        {
            _status = Status.HumanAway;
            return new TickResult(Status.HumanAway, false, null);
        }

        if (_status is not Status.Watching)
        {
            _quietSince = sample.Now;
            _status = Status.Watching;
        }
        else if (_quietSince is null)
        {
            _quietSince = sample.Now;
        }

        var quietFor = sample.Now - _quietSince!.Value;
        if (quietFor >= idle)
        {
            _awayLatched = true;
            _status = Status.HumanAway;
            return new TickResult(Status.HumanAway, EdgeHumanAway: true, quietFor);
        }

        _status = Status.Watching;
        return new TickResult(Status.Watching, false, quietFor);
    }

    static string NormalizeKind(string? raw)
    {
        var a = (raw ?? "").Trim().ToLowerInvariant();
        if (a.Contains("stop", StringComparison.Ordinal)) return "stop";
        if (a.Contains("queue", StringComparison.Ordinal)) return "queue";
        if (a.Contains("send", StringComparison.Ordinal)) return "send";
        if (a.Contains("voice", StringComparison.Ordinal)
            || a.Contains("microphone", StringComparison.Ordinal)
            || a.Contains("mic", StringComparison.Ordinal))
            return "voice";
        if (a.Length == 0) return "empty";
        return a switch
        {
            "voice" or "send" or "stop" or "queue" or "empty" or "other" => a,
            _ => a
        };
    }

    static string NormalizeText(string? raw) =>
        (raw ?? "").Replace('\u00a0', ' ').Trim();
}
