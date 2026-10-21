using System.Text.Json;
using TerminalMcp.Core;
using Xunit;

namespace CdpMcp.Tests;

/// <summary>
/// Адрес доставки — владелец. Арм помнит ник линии (seat из реестра или явный owner=)
/// и перед будильником проверяет, что владелец всё ещё ведёт ту же сессию. Иначе — отказ,
/// а не будильник чужой линии (инцидент tm-wake: 2026-10-12, 2026-10-21).
/// </summary>
[Collection("BatchSerial")]
public sealed class IgniteArmOwnerRoutingTests : IDisposable
{
    /// <summary>Сессия из герметичного тестового ростера (Тестовик → эта сессия).</summary>
    const string Session = "ses_test0000000000000000000";

    public IgniteArmOwnerRoutingTests()
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

    static Dictionary<string, JsonElement> ArmArgs(string id, string owner)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement("arm"),
            ["when"] = JsonSerializer.SerializeToElement("shell_finished"),
            ["id"] = JsonSerializer.SerializeToElement(id),
            ["task"] = JsonSerializer.SerializeToElement("owner probe"),
            ["harness"] = JsonSerializer.SerializeToElement("opencode"),
            ["session"] = JsonSerializer.SerializeToElement(Session),
            ["owner"] = JsonSerializer.SerializeToElement(owner),
            ["settle_seconds"] = JsonSerializer.SerializeToElement(0),
            ["ok_only"] = JsonSerializer.SerializeToElement(false),
            ["once"] = JsonSerializer.SerializeToElement(false),
        };
        return args;
    }

    static void FireShellFinished() =>
        IdeShellIgnite.OnShellFinished(new ShellFinishedInfo(
            "probe", "python x", "C:\\", 0, Background: true, DateTimeOffset.UtcNow));

    static IdeIgniteArmHost.IgniteArm? WaitForSettled(string id)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var arm = IdeIgniteArmHost.Snapshot().FirstOrDefault(a => a.Id == id);
            if (arm is not null && (arm.SendOk is not null || arm.Status == "error"))
                return arm;
            Thread.Sleep(50);
        }
        return IdeIgniteArmHost.Snapshot().FirstOrDefault(a => a.Id == id);
    }

    [Fact]
    public void Arm_keeps_owner_nick_from_args()
    {
        var id = "owner-probe-args-" + Guid.NewGuid().ToString("N")[..6];
        try
        {
            IdeIgniteChannel.Handle(ArmArgs(id, "Тестовик"));
            var armed = IdeIgniteArmHost.Snapshot().FirstOrDefault(a => a.Id == id);
            Assert.NotNull(armed);
            Assert.Equal("Тестовик", armed!.OwnerNick);
            Assert.Equal(Session, armed.OpencodeSession);
        }
        finally
        {
            Disarm(id);
        }
    }

    [Fact]
    public void Unknown_owner_is_refused_before_delivery()
    {
        var id = "owner-probe-unknown-" + Guid.NewGuid().ToString("N")[..6];
        try
        {
            IdeIgniteChannel.Handle(ArmArgs(id, "НикНеВРеестре"));
            FireShellFinished();

            // Статус арма после отказа обязан стать терминальным, а не остаться «firing».
            var seen = new List<string>();
            var deadline = DateTime.UtcNow.AddSeconds(8);
            IdeIgniteArmHost.IgniteArm? after = null;
            while (DateTime.UtcNow < deadline)
            {
                var arm = IdeIgniteArmHost.Snapshot().FirstOrDefault(a => a.Id == id);
                if (arm is not null)
                {
                    if (seen.Count == 0 || seen[^1] != arm.Status)
                        seen.Add(arm.Status);
                    after = arm;
                    if (arm.Status == "error" || arm.Status == "awaiting" || arm.Status == "armed")
                        break;
                }
                Thread.Sleep(100);
            }

            Assert.NotNull(after);
            Assert.Equal(false, after!.SendOk);
            Assert.Equal("no_owner", after.SendError);
            Assert.True(
                after.Status is "error" or "awaiting" or "armed",
                $"status={after.Status} seen=[{string.Join("→", seen)}] sendOk={after.SendOk} sendError={after.SendError}");
        }
        finally
        {
            Disarm(id);
        }
    }

    [Fact]
    public void Owner_from_roster_delivers()
    {
        var id = "owner-probe-ok-" + Guid.NewGuid().ToString("N")[..6];
        try
        {
            IdeIgniteChannel.Handle(ArmArgs(id, "Тестовик"));
            FireShellFinished();

            var after = WaitForSettled(id);
            Assert.NotNull(after);
            Assert.Equal(true, after!.SendOk);
            Assert.NotEqual("error", after.Status);
        }
        finally
        {
            Disarm(id);
        }
    }

    static void Disarm(string id) =>
        _ = IdeIgniteArmHost.Disarm(new Dictionary<string, JsonElement>
        {
            ["id"] = JsonSerializer.SerializeToElement(id),
            ["force"] = JsonSerializer.SerializeToElement(true)
        });
}
