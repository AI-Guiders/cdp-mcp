#nullable enable

namespace CdpMcp;

/// <summary>
/// Human-in-the-loop detector (pure FSM).
/// Voice/empty Composer + no text for <see cref="DefaultIdle"/> → edge <c>human_away</c> once per spell.
/// Composer text / Send resets; Stop/Queue ends the spell.
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
    bool _firedThisSpell;

    public Status Current => _status;
    public bool FiredThisSpell => _firedThisSpell;
    public DateTimeOffset? QuietSince => _quietSince;

    public void Reset()
    {
        _status = Status.Idle;
        _quietSince = null;
        _lastText = "";
        _firedThisSpell = false;
    }

    public TickResult Tick(Sample sample)
    {
        var idle = sample.Idle ?? DefaultIdle;
        if (idle <= TimeSpan.Zero)
            idle = DefaultIdle;

        var kind = NormalizeKind(sample.ButtonKind);
        var text = NormalizeText(sample.InputText);

        // Agent flying — not a human-hold window.
        if (kind is "stop" or "queue")
        {
            Reset();
            return new TickResult(Status.Idle, false, null);
        }

        // Composer text is the primary presence signal (PM: more reliable than Voice alone).
        if (text.Length > 0 || kind == "send")
        {
            _lastText = text;
            _quietSince = sample.Now;
            _firedThisSpell = false;
            _status = Status.HumanPresent;
            return new TickResult(Status.HumanPresent, false, TimeSpan.Zero);
        }

        // Empty draft / Voice / mic idle — start or continue quiet watch.
        _lastText = text;
        if (_status is not (Status.Watching or Status.HumanAway))
        {
            _quietSince = sample.Now;
            _status = Status.Watching;
            _firedThisSpell = false;
        }
        else if (_quietSince is null)
        {
            _quietSince = sample.Now;
            _status = Status.Watching;
        }

        if (_firedThisSpell)
        {
            _status = Status.HumanAway;
            return new TickResult(Status.HumanAway, false, sample.Now - _quietSince);
        }

        var quietFor = sample.Now - _quietSince!.Value;
        if (quietFor >= idle)
        {
            _firedThisSpell = true;
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
