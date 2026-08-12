#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// Multi-turn dialog memory for <see cref="CitizenTurnMode.Dialog"/>.
/// Seat file under StateRoot/{seat}/citizen-dialog.jsonl — survives remount; wire mode ignores.
/// </summary>
internal static class CitizenDialogHistory
{
    public const string FileName = "citizen-dialog.jsonl";
    public const int DefaultMaxMessages = 40; // prose pairs + tool rounds — turn-edge peel
    /// <summary>Char budget for Load() — ≥3 clipped tool results (4k each) + prose; trim oldest.</summary>
    public const int DefaultMaxChars = 18_000;

    static readonly object Gate = new();
    static string? PathOverrideForTests;
    static List<CitizenCompletions.ChatMessage>? MemoryOverrideForTests;
    static string? LastAppendErrorForTests;
    static string? ActiveModelOverride;

    /// <summary>Live model slot — partitions dialog file so switch ≠ inherited memory.</summary>
    public static string? ActiveModel
    {
        get
        {
            lock (Gate)
                return ActiveModelOverride;
        }
        set
        {
            lock (Gate)
                ActiveModelOverride = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }

    public static string SeatDir =>
        Path.Combine(CdpProfile.StateRoot, IdeIgniteArmHost.Seat);

    public static string FilePath =>
        PathOverrideForTests
        ?? Path.Combine(SeatDir, ResolveFileName(ActiveModel));

    static string ResolveFileName(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return FileName;
        return "citizen-dialog." + CitizenIdentity.SanitizeModelKey(model) + ".jsonl";
    }

    /// <summary>Tests: redirect file or force in-memory list.</summary>
    internal static void SetTestPath(string? path) => PathOverrideForTests = path;

    internal static void SetTestMemory(List<CitizenCompletions.ChatMessage>? msgs)
    {
        lock (Gate)
            MemoryOverrideForTests = msgs;
    }

    internal static void ResetForTests()
    {
        lock (Gate)
        {
            PathOverrideForTests = null;
            MemoryOverrideForTests = null;
            LastAppendErrorForTests = null;
            ActiveModelOverride = null;
        }
    }

    internal static string? LastAppendError
    {
        get
        {
            lock (Gate)
                return LastAppendErrorForTests;
        }
    }

    public static IReadOnlyList<CitizenCompletions.ChatMessage> Load(
        int maxMessages = DefaultMaxMessages,
        int maxChars = DefaultMaxChars)
    {
        lock (Gate)
        {
            if (MemoryOverrideForTests is not null)
                return TrimNewest(MemoryOverrideForTests, maxMessages, maxChars);
            return LoadUnlocked(maxMessages, maxChars);
        }
    }

    static IReadOnlyList<CitizenCompletions.ChatMessage> LoadUnlocked(
        int maxMessages = DefaultMaxMessages,
        int maxChars = DefaultMaxChars)
    {
        var path = FilePath;
        if (!File.Exists(path))
            return [];

        try
        {
            var list = new List<CitizenCompletions.ChatMessage>();
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var role = root.TryGetProperty("role", out var r) ? r.GetString() : null;
                var content = root.TryGetProperty("content", out var c) ? c.GetString() : null;
                if (string.IsNullOrWhiteSpace(role) || string.IsNullOrWhiteSpace(content))
                    continue;
                // tool = prior hands (MEAI dig) — without these, turn N+1 re-digs blind.
                if (role is not ("user" or "assistant" or "tool"))
                    continue;
                list.Add(new CitizenCompletions.ChatMessage(role, content));
            }

            return TrimNewest(list, maxMessages, maxChars);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Keep newest messages under both count and char budgets (pairs stay intact when possible).</summary>
    internal static IReadOnlyList<CitizenCompletions.ChatMessage> TrimNewest(
        IReadOnlyList<CitizenCompletions.ChatMessage> source,
        int maxMessages,
        int maxChars)
    {
        if (source.Count == 0)
            return [];

        maxMessages = Math.Max(0, maxMessages);
        maxChars = Math.Max(0, maxChars);
        if (maxMessages == 0 || maxChars == 0)
            return [];

        var keep = new List<CitizenCompletions.ChatMessage>();
        var chars = 0;
        for (var i = source.Count - 1; i >= 0; i--)
        {
            var m = source[i];
            var len = m.Content?.Length ?? 0;
            if (keep.Count >= maxMessages)
                break;
            if (chars + len > maxChars && keep.Count > 0)
                break;
            keep.Add(m);
            chars += len;
        }

        keep.Reverse();
        // Prefer not to start mid-pair: drop orphan leading assistant/tool before first user.
        while (keep.Count >= 2 && keep[0].Role is "assistant" or "tool")
            keep.RemoveAt(0);
        return keep;
    }

    public static void Append(
        string userText,
        string assistantText,
        IReadOnlyList<ToolRound>? tools = null,
        int maxMessages = DefaultMaxMessages)
    {
        if (string.IsNullOrWhiteSpace(userText) || string.IsNullOrWhiteSpace(assistantText))
            return;

        lock (Gate)
        {
            var batch = new List<CitizenCompletions.ChatMessage>
            {
                new("user", userText.Trim())
            };
            if (tools is { Count: > 0 })
            {
                foreach (var t in tools)
                {
                    if (string.IsNullOrWhiteSpace(t.Content))
                        continue;
                    batch.Add(new CitizenCompletions.ChatMessage("tool", FormatToolContent(t)));
                }
            }

            batch.Add(new CitizenCompletions.ChatMessage("assistant", assistantText.Trim()));

            if (MemoryOverrideForTests is not null)
            {
                MemoryOverrideForTests.AddRange(batch);
                var trimmed = TrimNewest(MemoryOverrideForTests, maxMessages, DefaultMaxChars);
                MemoryOverrideForTests.Clear();
                MemoryOverrideForTests.AddRange(trimmed);
                return;
            }

            LastAppendErrorForTests = null;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                    var sbWrite = new System.Text.StringBuilder();
                    var at = DateTimeOffset.UtcNow;
                    foreach (var m in batch)
                        sbWrite.AppendLine(JsonSerializer.Serialize(new { role = m.Role, content = m.Content, at_utc = at }));
                    File.AppendAllText(FilePath, sbWrite.ToString());

                    // Trim file if oversized (count + char budgets) — drop oldest, keep newest tools.
                    var loaded = LoadUnlocked(maxMessages * 4, DefaultMaxChars * 4);
                    var keep = TrimNewest(loaded, maxMessages, DefaultMaxChars);
                    if (keep.Count < loaded.Count)
                    {
                        var sb = new System.Text.StringBuilder();
                        foreach (var m in keep)
                            sb.AppendLine(JsonSerializer.Serialize(new { role = m.Role, content = m.Content, at_utc = DateTimeOffset.UtcNow }));
                        File.WriteAllText(FilePath, sb.ToString());
                    }

                    return;
                }
                catch (Exception ex)
                {
                    LastAppendErrorForTests = ex.GetType().Name;
                    if (attempt == 0)
                        Thread.Sleep(15);
                }
            }
        }
    }

    /// <summary>One MEAI/tool dig persisted across turns (role=tool in jsonl).</summary>
    public sealed record ToolRound(string Name, bool Ok, string Content);

    internal static string FormatToolContent(ToolRound t)
    {
        var status = t.Ok ? "ok" : "fail";
        var body = t.Content.Trim();
        return $"[tool_status={status} name={t.Name}]\n{body}";
    }

    public static void Clear()
    {
        lock (Gate)
        {
            MemoryOverrideForTests?.Clear();
            LastAppendErrorForTests = null;

            try
            {
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
            }
            catch
            {
                // ignore
            }
        }
    }

    public static object Pulse()
    {
        var msgs = Load();
        string? lastUser = null;
        string? lastAssistant = null;
        var tools = 0;
        for (var i = msgs.Count - 1; i >= 0; i--)
        {
            if (msgs[i].Role == "tool")
                tools++;
            if (lastAssistant is null && msgs[i].Role == "assistant")
                lastAssistant = Trunc(msgs[i].Content, 120);
            if (lastUser is null && msgs[i].Role == "user")
                lastUser = Trunc(msgs[i].Content, 120);
            if (lastUser is not null && lastAssistant is not null && i == 0)
                break;
        }

        var prose = msgs.Count(m => m.Role is "user" or "assistant");
        return new
        {
            path = FilePath,
            count = msgs.Count,
            pairs = prose / 2,
            tools,
            last_role = msgs.Count > 0 ? msgs[^1].Role : null,
            last_user = lastUser,
            last_assistant = lastAssistant,
            last_append_error = LastAppendErrorForTests
        };
    }

    /// <summary>Afferent line for dialog turns — ADCM pressure when fat (Prune/Partition/Persist/Rebuild).</summary>
    public static string AfferentLine()
    {
        var msgs = Load();
        var prose = msgs.Count(m => m.Role is "user" or "assistant");
        var pairs = prose / 2;
        var tools = msgs.Count(m => m.Role == "tool");
        var chars = 0;
        foreach (var m in msgs)
            chars += m.Content?.Length ?? 0;

        if (pairs <= 0 && tools <= 0)
            return "dialog | pairs=0 · fresh thread (still durable after remount) · ADCM=@intent dialog partition|persist|rebuild|clear";

        var fat = pairs >= 4 || tools >= 6 || chars >= 12_000;
        var tip = fat
            ? " · pressure FAT — Prune=@intent dialog clear · Partition=@intent dialog partition · Persist=@intent dialog persist key= v= · Rebuild=@intent dialog rebuild (anti-poison) · dig=@intent pressure|plan|domain"
            : " · ADCM=@intent dialog clear|partition|persist|rebuild";
        return $"dialog | pairs={pairs} · tools={tools} · msgs={msgs.Count} · chars≈{chars} · prior turns+hands in messages — use them; do not re-dig the same path{tip}";
    }

    static string Trunc(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";
}
