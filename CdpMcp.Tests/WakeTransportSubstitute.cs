#nullable enable
using System.Runtime.CompilerServices;
using NSubstitute;

namespace CdpMcp.Tests;

/// <summary>
/// ADR-0219 P1 — тесты НЕ спавнят реальные opencode run'ы (класс «пустых user-ходов»).
/// ModuleInitializer подменяет транспорт на NSubstitute-сабститут:
/// IsSessionBusy — реальный (чтение лога, безобидно), SendCliAsync — заглушка ok.
/// Тесты, которым нужен реальный транспорт, ставят RealOpencodeWakeTransport.Instance явно.
/// </summary>
internal static class WakeTransportSubstitute
{
    [ModuleInitializer]
    internal static void Init()
    {
        var real = RealOpencodeWakeTransport.Instance;
        var sub = Substitute.For<IOpencodeWakeTransport>();
        sub.IsSessionBusy(Arg.Any<string>()).Returns(x => real.IsSessionBusy((string)x[0]!));
        sub.SendCliAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<object>(new { ok = true, mode = "cli", detail = "substituted (NSubstitute)" }));
        CideWakeChannels.Opencode.Transport = sub;

        // Герметичный ростер: ровно ОДНА живая opencode-линия → ResolveDefaultSeat работает,
        // тесты не трогают боевой intercom-agents.witdb (мульти-линейный мир → ambiguous_live_line).
        var roster = Path.Combine(Path.GetTempPath(), "cdp-mcp-test-roster.witdb");
        File.WriteAllText(roster,
            "2026-09-08T00:00:00Z\tnick=Тестовик\tkind=guest\tline=line-test\tharness=opencode\tsession=ses_test0000000000000000000\n");
        CideIntercomAgents.WitDbPathOverride = () => roster;

        // Герметичный CDP-стор: тесты не пишут в боевой %LocalAppData%/cdp-mcp/cdp-state.witdb.
        // Без этого прогон тестов оставлял там свои армы (shell-bg-* → opencode_session=<placeholder>),
        // и живой CdpService их подхватывал: opencode run -s <placeholder> "<charge>" — будилась реальная линия.
        TestStateRoot = Path.Combine(Path.GetTempPath(), "cdp-mcp-test-state", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TestStateRoot);
        CdpStateStoreOverride.ForTests = new WitDbCdpStateStore(new FixedStateRoot(TestStateRoot));
    }

    /// <summary>Temp-root изолированного CDP-стора (см. Init).</summary>
    internal static string TestStateRoot { get; private set; } = "";

    sealed class FixedStateRoot(string root) : IStateRootProvider
    {
        public string ResolveStateRoot() => root;
    }
}
