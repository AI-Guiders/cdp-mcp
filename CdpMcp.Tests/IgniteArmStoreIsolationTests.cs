using System.Text.Json;
using Cdp.CdpState;
using Xunit;

namespace CdpMcp.Tests;

/// <summary>
/// Тесты не пишут армы в боевой cdp-state.witdb: прогон тестов оставлял там свои армы
/// (shell-bg-* → opencode_session=&lt;placeholder&gt;), и живой CdpService их подхватывал —
/// opencode run -s &lt;placeholder&gt; "&lt;charge&gt;", то есть будилась реальная линия.
/// Регресс: арм, поднятый в тесте, лежит в изолированном temp-сторе.
/// </summary>
[Collection("BatchSerial")]
public sealed class IgniteArmStoreIsolationTests : IDisposable
{
    public IgniteArmStoreIsolationTests()
    {
        IdeToolCallWatch.SuppressArmForTests = false;
        IdeIgniteArmHost.SetAutonomous(false, "test_setup");
        IdeIgniteArmHost.SetHild(false, "test_setup");
    }

    public void Dispose()
    {
        IdeToolCallWatch.SuppressArmForTests = false;
        IdeIgniteArmHost.SetAutonomous(false, "test_dispose");
        IdeIgniteArmHost.SetHild(false, "test_dispose");
    }

    [Fact]
    public void Armed_test_arm_lands_in_isolated_state_store()
    {
        var armId = "shell-bg-isolation-" + Guid.NewGuid().ToString("N")[..6];
        try
        {
            IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("arm"),
                ["when"] = JsonSerializer.SerializeToElement("shell_finished"),
                ["id"] = JsonSerializer.SerializeToElement(armId),
                ["task"] = JsonSerializer.SerializeToElement("isolation probe"),
                ["settle_seconds"] = JsonSerializer.SerializeToElement(0),
                ["ok_only"] = JsonSerializer.SerializeToElement(false),
            });

            Assert.NotNull(CdpStateStoreOverride.ForTests);

            var seat = IdeIgniteArmHost.Seat;
            var stored = new CdpStateStore(WakeTransportSubstitute.TestStateRoot).LoadArms(seat);
            Assert.Contains(stored, a => a.Id.Equals(armId, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _ = IdeIgniteArmHost.Disarm(new Dictionary<string, JsonElement>
            {
                ["all"] = JsonSerializer.SerializeToElement(true),
                ["force"] = JsonSerializer.SerializeToElement(true)
            });
        }
    }
}
