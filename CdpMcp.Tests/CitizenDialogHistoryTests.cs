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
}
