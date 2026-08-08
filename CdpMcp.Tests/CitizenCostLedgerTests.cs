#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenCompletionsSerial")]
public sealed class CitizenCostLedgerTests
{
    [Fact]
    public void Record_and_Pulse_accumulate_session_totals()
    {
        CitizenCostLedger.ResetForTests();
        CitizenCostLedger.SetTestMemory(null, null, memoryOnly: true);
        try
        {
            var built = CitizenCompletions.Build("hi", inject: false, mode: CitizenTurnMode.Dialog, history: false);
            CitizenCostLedger.Record(built, "m", "openai_compat", ok: true, error: null, 100, 20, 120);
            CitizenCostLedger.Record(built, "m", "openai_compat", ok: true, error: null, 110, 30, 140);

            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(CitizenCostLedger.Pulse()));
            var root = doc.RootElement;
            Assert.Equal(2, root.GetProperty("turns").GetInt32());
            Assert.Equal(210, root.GetProperty("prompt_tokens").GetInt64());
            Assert.Equal(50, root.GetProperty("completion_tokens").GetInt64());
            Assert.Contains("cost · turns=2", CitizenCostLedger.PulseLine(), StringComparison.Ordinal);
        }
        finally
        {
            CitizenCostLedger.ResetForTests();
        }
    }
}
