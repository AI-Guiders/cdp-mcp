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
    public const int DefaultMaxMessages = 40; // 20 user/assistant pairs

    static readonly object Gate = new();
    static string? PathOverrideForTests;
    static List<CitizenCompletions.ChatMessage>? MemoryOverrideForTests;

    public static string SeatDir =>
        Path.Combine(CdpProfile.StateRoot, IdeIgniteArmHost.Seat);

    public static string FilePath =>
        PathOverrideForTests ?? Path.Combine(SeatDir, FileName);

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
        }
    }

    public static IReadOnlyList<CitizenCompletions.ChatMessage> Load(int maxMessages = DefaultMaxMessages)
    {
        lock (Gate)
        {
            if (MemoryOverrideForTests is not null)
                return MemoryOverrideForTests.TakeLast(Math.Max(0, maxMessages)).ToArray();
        }

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
                if (role is not ("user" or "assistant"))
                    continue;
                list.Add(new CitizenCompletions.ChatMessage(role, content));
            }

            if (list.Count <= maxMessages)
                return list;
            return list.TakeLast(maxMessages).ToArray();
        }
        catch
        {
            return [];
        }
    }

    public static void Append(string userText, string assistantText, int maxMessages = DefaultMaxMessages)
    {
        if (string.IsNullOrWhiteSpace(userText) || string.IsNullOrWhiteSpace(assistantText))
            return;

        lock (Gate)
        {
            if (MemoryOverrideForTests is not null)
            {
                MemoryOverrideForTests.Add(new CitizenCompletions.ChatMessage("user", userText.Trim()));
                MemoryOverrideForTests.Add(new CitizenCompletions.ChatMessage("assistant", assistantText.Trim()));
                while (MemoryOverrideForTests.Count > maxMessages)
                    MemoryOverrideForTests.RemoveAt(0);
                return;
            }
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var lines =
                JsonSerializer.Serialize(new { role = "user", content = userText.Trim(), at_utc = DateTimeOffset.UtcNow })
                + Environment.NewLine
                + JsonSerializer.Serialize(new { role = "assistant", content = assistantText.Trim(), at_utc = DateTimeOffset.UtcNow })
                + Environment.NewLine;
            File.AppendAllText(FilePath, lines);

            // Trim file if oversized (rewrite last N).
            var loaded = Load(maxMessages * 4); // read raw-ish then rewrite capped
            if (loaded.Count > maxMessages)
            {
                var keep = loaded.TakeLast(maxMessages).ToArray();
                var sb = new System.Text.StringBuilder();
                foreach (var m in keep)
                    sb.AppendLine(JsonSerializer.Serialize(new { role = m.Role, content = m.Content, at_utc = DateTimeOffset.UtcNow }));
                File.WriteAllText(FilePath, sb.ToString());
            }
        }
        catch
        {
            // Dialog history is best-effort — never fail the turn.
        }
    }

    public static void Clear()
    {
        lock (Gate)
        {
            MemoryOverrideForTests?.Clear();
        }

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

    public static object Pulse()
    {
        var msgs = Load();
        string? lastUser = null;
        string? lastAssistant = null;
        for (var i = msgs.Count - 1; i >= 0; i--)
        {
            if (lastAssistant is null && msgs[i].Role == "assistant")
                lastAssistant = Trunc(msgs[i].Content, 120);
            if (lastUser is null && msgs[i].Role == "user")
                lastUser = Trunc(msgs[i].Content, 120);
            if (lastUser is not null && lastAssistant is not null)
                break;
        }

        return new
        {
            path = FilePath,
            count = msgs.Count,
            pairs = msgs.Count / 2,
            last_role = msgs.Count > 0 ? msgs[^1].Role : null,
            last_user = lastUser,
            last_assistant = lastAssistant
        };
    }

    /// <summary>Afferent line for dialog turns — reminds FM that priors are in context.</summary>
    public static string AfferentLine()
    {
        var msgs = Load();
        var pairs = msgs.Count / 2;
        return pairs > 0
            ? $"dialog | pairs={pairs} · msgs={msgs.Count} · prior turns in messages — use them; do not claim amnesia"
            : "dialog | pairs=0 · fresh thread (still durable after remount)";
    }

    static string Trunc(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";
}
