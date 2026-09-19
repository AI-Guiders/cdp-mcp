#nullable enable
namespace CdpMcp;

/// <summary>
/// Process-static same-wake counters (L5). Soft recalibration substrate — no blame language.
/// </summary>
internal static class IdeSameWakeLatch
{
    static readonly object Gate = new();
    static int _bufferOps;
    static bool _recallDone;
    static bool _ignoreNudgeSpent;
    static bool _earlyActNoted;

    public static int BufferOps
    {
        get { lock (Gate) return _bufferOps; }
    }

    public static bool RecallDone
    {
        get { lock (Gate) return _recallDone; }
    }

    public static bool IgnoreNudgeSpent
    {
        get { lock (Gate) return _ignoreNudgeSpent; }
    }

    public static readonly string[] AwarenessPatterns =
    [
        "buffer_mill — ≥5 buffer ops same wake → go=inventory before more edits",
        "ignore_hint — first mutate without recall (full wake) → one soft nudge",
        "early_act — act/mutate before explore inventory → go=ground then inventory",
        "biped_mill — alert.biped_mill → inventory + wave seed (desk already)",
        "thrash_hot — set_text ring → edit_draft / scope (L3)",
        "pressure_stash — compact armed → recall before resume",
        "equal_standing — operator ≠ patch queue; habitat bias → patch SSOT this wake"
    ];

    public static void NoteBufferOp()
    {
        lock (Gate)
            _bufferOps++;
    }

    public static void NoteRecall()
    {
        lock (Gate)
            _recallDone = true;
    }

    public static void NoteEarlyAct()
    {
        lock (Gate)
            _earlyActNoted = true;
    }

    /// <summary>One soft nudge per wake when full-wake mutate precedes recall. Returns hint or null.</summary>
    public static string? TryConsumeIgnoreHintNudge(bool fullWake)
    {
        if (!fullWake) return null;
        lock (Gate)
        {
            if (_recallDone || _ignoreNudgeSpent)
                return null;
            _ignoreNudgeSpent = true;
            return "ignore_hint — soft: run cdp_pressure op=recall (or go=ground) once, then resume mutate; not a block.";
        }
    }

    public static void Reset()
    {
        lock (Gate)
        {
            _bufferOps = 0;
            _recallDone = false;
            _ignoreNudgeSpent = false;
            _earlyActNoted = false;
        }
    }

    public static string PulseLine()
    {
        var d = Detect();
        return d.Pulse;
    }

    public static GroundCard Detect()
    {
        lock (Gate)
        {
            if (_bufferOps >= IdeGroundChannel.BufferMillThreshold)
            {
                return new GroundCard(
                    "buffer_mill",
                    $"ground · buffer_mill ×{_bufferOps}",
                    "inventory",
                    ResumeOk: true);
            }

            if (_earlyActNoted && !_recallDone)
            {
                return new GroundCard(
                    "early_act",
                    "ground · early_act",
                    "inventory",
                    ResumeOk: true);
            }

            if (!_recallDone && _ignoreNudgeSpent)
            {
                return new GroundCard(
                    "ignore_hint",
                    "ground · ignore_hint (nudged)",
                    "pressure",
                    ResumeOk: true);
            }

            return new GroundCard(
                "ok",
                $"ground · ok · buf={_bufferOps} recall={(_recallDone ? "yes" : "no")}",
                "ground",
                ResumeOk: true);
        }
    }

    public readonly record struct GroundCard(string Pattern, string Pulse, string Next, bool ResumeOk);
}
