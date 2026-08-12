#nullable enable
using Xunit;

namespace CdpMcp.Tests;

public sealed class CitizenDialogHistoryTests
{
    [Fact]
    public void TrimNewest_keeps_newest_under_char_budget()
    {
        var msgs = new List<CitizenCompletions.ChatMessage>
        {
            new("user", new string('a', 4000)),
            new("assistant", new string('b', 4000)),
            new("user", "ping"),
            new("assistant", "pong"),
        };

        var keep = CitizenDialogHistory.TrimNewest(msgs, maxMessages: 40, maxChars: 500);
        Assert.Equal(2, keep.Count);
        Assert.Equal("ping", keep[0].Content);
        Assert.Equal("pong", keep[1].Content);
        Assert.True(keep.Sum(m => m.Content.Length) <= 500);
    }

    [Fact]
    public void TrimNewest_respects_message_count()
    {
        var msgs = Enumerable.Range(0, 10)
            .SelectMany(i => new[]
            {
                new CitizenCompletions.ChatMessage("user", $"u{i}"),
                new CitizenCompletions.ChatMessage("assistant", $"a{i}"),
            })
            .ToList();

        var keep = CitizenDialogHistory.TrimNewest(msgs, maxMessages: 4, maxChars: 100_000);
        Assert.Equal(4, keep.Count);
        Assert.Equal("u8", keep[0].Content);
        Assert.Equal("a9", keep[3].Content);
    }

    [Fact]
    public void Append_persists_tool_rounds_and_Load_returns_them()
    {
        CitizenDialogHistory.ResetForTests();
        var path = Path.Combine(Path.GetTempPath(), "cdp-dlg-" + Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            CitizenDialogHistory.SetTestPath(path);
            CitizenDialogHistory.Append(
                "find X",
                "done",
                [
                    new CitizenDialogHistory.ToolRound("find", true, "hit path=Foo.cs"),
                    new CitizenDialogHistory.ToolRound("cdp_open", true, "opened Foo.cs")
                ]);

            var msgs = CitizenDialogHistory.Load(maxMessages: 40, maxChars: 18_000);
            Assert.Equal(4, msgs.Count);
            Assert.Equal("user", msgs[0].Role);
            Assert.Equal("tool", msgs[1].Role);
            Assert.Contains("tool_status=ok", msgs[1].Content, StringComparison.Ordinal);
            Assert.Contains("hit path=Foo.cs", msgs[1].Content, StringComparison.Ordinal);
            Assert.Equal("tool", msgs[2].Role);
            Assert.Equal("assistant", msgs[3].Role);

            var built = CitizenCompletions.Build("next", history: true, mode: CitizenTurnMode.Dialog, inject: false);
            Assert.Contains(built.Messages, m => m.Role == "tool" && m.Content.Contains("Foo.cs", StringComparison.Ordinal));
            var meai = CitizenCompletions.BuildMeAiMessages(built);
            Assert.Contains(meai, m => m.Text?.Contains("[prior_hands]", StringComparison.Ordinal) == true);
        }
        finally
        {
            CitizenDialogHistory.ResetForTests();
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void DefaultMaxChars_fits_three_tool_clips()
    {
        Assert.True(CitizenDialogHistory.DefaultMaxChars >= 3 * CitizenMeAiAgentTools.AgentToolClipChars);
    }
}
