#nullable enable
using System.Globalization;
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// Thin live-turn cost ledger for citizen FM usage (SoftFL).
/// Append-only jsonl under StateRoot/{seat}/citizen-cost.jsonl + totals sidecar for scene pulse.
/// </summary>
internal static class CitizenCostLedger
{
    public const string FileName = "citizen-cost.jsonl";
    public const string TotalsFileName = "citizen-cost-totals.json";

    static readonly object Gate = new();
    static string? PathOverrideForTests;
    static string? TotalsOverrideForTests;
    static TotalsState MemoryTotalsForTests = TotalsState.Empty;
    static bool UseMemoryForTests;

    public static string SeatDir =>
        Path.Combine(CdpProfile.StateRoot, IdeIgniteArmHost.Seat);

    public static string FilePath =>
        PathOverrideForTests ?? Path.Combine(SeatDir, FileName);

    public static string TotalsPath =>
        TotalsOverrideForTests ?? Path.Combine(SeatDir, TotalsFileName);

    /// <summary>Tests: redirect paths or force in-memory totals.</summary>
    public static void SetTestMemory(string? jsonlPath, string? totalsPath, bool memoryOnly = false)
    {
        lock (Gate)
        {
            PathOverrideForTests = jsonlPath;
            TotalsOverrideForTests = totalsPath;
            UseMemoryForTests = memoryOnly;
            MemoryTotalsForTests = TotalsState.Empty;
        }
    }

    public static void ResetForTests()
    {
        lock (Gate)
        {
            PathOverrideForTests = null;
            TotalsOverrideForTests = null;
            UseMemoryForTests = false;
            MemoryTotalsForTests = TotalsState.Empty;
        }
    }

    /// <summary>Record one live provider turn (not dry_run).</summary>
    public static void Record(
        CitizenCompletions.BuiltTurn built,
        string? model,
        string? provider,
        bool ok,
        string? error,
        int? promptTokens,
        int? completionTokens,
        int? totalTokens)
    {
        var systemChars = built.System?.Length ?? 0;
        var msgs = built.Messages ?? Array.Empty<CitizenCompletions.ChatMessage>();
        // Last message is this turn's user; prior = history window in the request.
        var historyMsgs = Math.Max(0, msgs.Count - 1);
        var historyChars = 0;
        for (var i = 0; i < historyMsgs; i++)
            historyChars += msgs[i].Content?.Length ?? 0;
        var userChars = msgs.Count > 0 ? (msgs[^1].Content?.Length ?? 0) : 0;
        var bodyChars = systemChars + historyChars + userChars;
        var systemSharePct = bodyChars > 0
            ? (int)Math.Round(100.0 * systemChars / bodyChars)
            : 0;
        var historySharePct = bodyChars > 0
            ? (int)Math.Round(100.0 * historyChars / bodyChars)
            : 0;

        var total = totalTokens
            ?? ((promptTokens is int p && completionTokens is int c) ? p + c : null);

        var line = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["utc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            ["ok"] = ok,
            ["mode"] = built.Mode.ToString().ToLowerInvariant(),
            ["model"] = model,
            ["provider"] = provider,
            ["prompt_tokens"] = promptTokens,
            ["completion_tokens"] = completionTokens,
            ["total_tokens"] = total,
            ["system_chars"] = systemChars,
            ["history_msgs"] = historyMsgs,
            ["history_chars"] = historyChars,
            ["user_chars"] = userChars,
            ["message_count"] = msgs.Count,
            ["system_share_pct"] = systemSharePct,
            ["history_share_pct"] = historySharePct,
            ["error"] = error
        };

        lock (Gate)
        {
            if (!UseMemoryForTests)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                    File.AppendAllText(FilePath, JsonSerializer.Serialize(line) + "\n");
                }
                catch
                {
                    /* best-effort ledger — never fail the turn */
                }
            }

            var t = LoadTotalsUnlocked();
            t = t with
            {
                Turns = t.Turns + 1,
                Prompt = t.Prompt + (promptTokens ?? 0),
                Completion = t.Completion + (completionTokens ?? 0),
                Total = t.Total + (total ?? 0),
                SystemCharsSum = t.SystemCharsSum + systemChars,
                HistoryCharsSum = t.HistoryCharsSum + historyChars,
                HistoryMsgsSum = t.HistoryMsgsSum + historyMsgs,
                LastUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                LastPrompt = promptTokens,
                LastCompletion = completionTokens,
                LastSystemChars = systemChars,
                LastHistoryMsgs = historyMsgs,
                LastSystemSharePct = systemSharePct,
                LastHistorySharePct = historySharePct,
                LastMode = built.Mode.ToString().ToLowerInvariant()
            };
            SaveTotalsUnlocked(t);
        }
    }

    public static object Pulse()
    {
        lock (Gate)
        {
            var t = LoadTotalsUnlocked();
            var avgSystem = t.Turns > 0 ? (int)(t.SystemCharsSum / t.Turns) : 0;
            var avgHistMsgs = t.Turns > 0 ? Math.Round((double)t.HistoryMsgsSum / t.Turns, 1) : 0;
            var pulse = t.Turns == 0
                ? "cost · empty"
                : $"cost · turns={t.Turns} · prompt={t.Prompt} · completion={t.Completion} · last sys={t.LastSystemChars} hist_msgs={t.LastHistoryMsgs} share={t.LastSystemSharePct}/{t.LastHistorySharePct}%";
            return new
            {
                path = UseMemoryForTests ? "(memory)" : FilePath,
                turns = t.Turns,
                prompt_tokens = t.Prompt,
                completion_tokens = t.Completion,
                total_tokens = t.Total,
                avg_system_chars = avgSystem,
                avg_history_msgs = avgHistMsgs,
                last = t.Turns == 0
                    ? null
                    : new
                    {
                        utc = t.LastUtc,
                        mode = t.LastMode,
                        prompt_tokens = t.LastPrompt,
                        completion_tokens = t.LastCompletion,
                        system_chars = t.LastSystemChars,
                        history_msgs = t.LastHistoryMsgs,
                        system_share_pct = t.LastSystemSharePct,
                        history_share_pct = t.LastHistorySharePct
                    },
                pulse
            };
        }
    }

    public static string PulseLine()
    {
        lock (Gate)
        {
            var t = LoadTotalsUnlocked();
            return t.Turns == 0
                ? "cost · empty"
                : $"cost · turns={t.Turns} · prompt={t.Prompt} · completion={t.Completion}";
        }
    }

    static TotalsState LoadTotalsUnlocked()
    {
        if (UseMemoryForTests)
            return MemoryTotalsForTests;
        try
        {
            if (!File.Exists(TotalsPath))
                return TotalsState.Empty;
            var json = File.ReadAllText(TotalsPath);
            var t = JsonSerializer.Deserialize<TotalsState>(json);
            return t ?? TotalsState.Empty;
        }
        catch
        {
            return TotalsState.Empty;
        }
    }

    static void SaveTotalsUnlocked(TotalsState t)
    {
        if (UseMemoryForTests)
        {
            MemoryTotalsForTests = t;
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TotalsPath)!);
            var tmp = TotalsPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(t));
            File.Move(tmp, TotalsPath, overwrite: true);
        }
        catch
        {
            /* best-effort */
        }
    }

    sealed record TotalsState(
        int Turns,
        long Prompt,
        long Completion,
        long Total,
        long SystemCharsSum,
        long HistoryCharsSum,
        long HistoryMsgsSum,
        string? LastUtc,
        int? LastPrompt,
        int? LastCompletion,
        int? LastSystemChars,
        int? LastHistoryMsgs,
        int? LastSystemSharePct,
        int? LastHistorySharePct,
        string? LastMode)
    {
        public static TotalsState Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, null, null, null, null, null, null, null, null);
    }
}
