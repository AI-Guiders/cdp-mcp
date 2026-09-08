using Cdp.CdpState;
using NSubstitute;
using Xunit;

namespace CdpMcp.Tests;

/// <summary>
/// ADR-0219 L2a/2 — герметичные тесты CdpWakeDispatcher:
/// fake roster/transport (NSubstitute), реальный WitDbCdpStateStore на temp root,
/// управляемое время (TimeProvider). Ни один тест не трогает прод-очередь.
/// </summary>
public class CdpWakeDispatcherTests : IDisposable
{
    readonly string _root;
    readonly ICdpStateStore _store;
    readonly IAgentRoster _roster = Substitute.For<IAgentRoster>();
    readonly MutableTime _time = new();
    readonly CdpHabitatOptions _options = new() { WakeNickCooldownSeconds = 120, DefaultDeliveryCooldownSeconds = 15 };

    public CdpWakeDispatcherTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-wake-disp-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
        _store = new WitDbCdpStateStore(new FixedStateRoot(_root));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    CdpWakeDispatcher Dispatcher(IOpencodeWakeTransport? transport = null, CdpHabitatOptions? options = null) => new(
        _roster,
        transport ?? Substitute.For<IOpencodeWakeTransport>(),
        _store,
        stateRoot: new FixedStateRoot(_root),
        time: _time,
        options: options ?? _options);

    static IOpencodeWakeTransport OkTransport(bool busy = false)
    {
        var t = Substitute.For<IOpencodeWakeTransport>();
        t.IsSessionBusy(Arg.Any<string>()).Returns(busy);
        t.SendCliAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<object>(new { ok = true, detail = "cli" }));
        return t;
    }

    [Fact]
    public async Task Delivers_pending_letter_and_marks_store()
    {
        var d = Dispatcher(OkTransport());
        _ = d.Enqueue(CdpWakeDispatcher.KindLetter, "привет", nick: "Тень", session: "ses_x", harness: "opencode");

        await d.TickAsync(CancellationToken.None);

        var delivered = _store.LoadWake("delivered");
        Assert.Single(delivered);
        Assert.Equal("cli", delivered[0].Detail);
        Assert.NotNull(delivered[0].DeliveredUtc);
        Assert.Equal(0, _store.LoadWake("pending").Count);
    }

    [Fact]
    public async Task Busy_session_stays_pending_with_detail()
    {
        var d = Dispatcher(OkTransport(busy: true));
        _ = d.Enqueue(CdpWakeDispatcher.KindLetter, "привет", nick: "Тень", session: "ses_x", harness: "opencode");

        await d.TickAsync(CancellationToken.None);

        var pending = _store.LoadWake("pending");
        Assert.Single(pending);
        Assert.Contains("session_busy", pending[0].Detail);
    }

    [Fact]
    public async Task Self_echo_is_skipped_without_transport()
    {
        var transport = OkTransport();
        var d = Dispatcher(transport);
        _ = d.Enqueue(CdpWakeDispatcher.KindLetter, "сам себе", nick: "Тень", from: "Тень", session: "ses_x", harness: "opencode");

        await d.TickAsync(CancellationToken.None);

        var skipped = _store.LoadWake("skipped");
        Assert.Single(skipped);
        Assert.Equal("self_echo", skipped[0].SkippedReason);
        await transport.DidNotReceive().SendCliAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Stopped_queue_skips_delivery()
    {
        var transport = OkTransport();
        var d = Dispatcher(transport);
        d.SetStopped(true);
        _ = d.Enqueue(CdpWakeDispatcher.KindLetter, "привет", nick: "Тень", session: "ses_x", harness: "opencode");

        await d.TickAsync(CancellationToken.None);

        Assert.Equal(0, _store.LoadWake("delivered").Count);
        Assert.Equal(1, _store.LoadWake("pending").Count);
        Assert.True(d.Stopped);
        d.SetStopped(false);
        Assert.False(d.Stopped);
    }

    [Fact]
    public async Task Per_nick_cooldown_holds_second_knock()
    {
        var transport = OkTransport();
        var d = Dispatcher(transport);
        _ = d.Enqueue(CdpWakeDispatcher.KindLetter, "первое", nick: "Тень", session: "ses_x", harness: "opencode");
        _ = d.Enqueue(CdpWakeDispatcher.KindLetter, "второе", nick: "Ток", session: "ses_y", harness: "opencode");

        await d.TickAsync(CancellationToken.None);
        Assert.Equal(1, _store.LoadWake("delivered").Count); // глобальный cooldown 15с держит второй конверт в этом тике

        _time.Advance(TimeSpan.FromSeconds(16));
        await d.TickAsync(CancellationToken.None);
        Assert.Equal(2, _store.LoadWake("delivered").Count);

        _ = d.Enqueue(CdpWakeDispatcher.KindLetter, "сразу снова", nick: "Тень", session: "ses_x", harness: "opencode");
        await d.TickAsync(CancellationToken.None); // nick cooldown 120s с доставки №1 (t0+16 < 120) — не доставит
        Assert.Equal(2, _store.LoadWake("delivered").Count);

        _time.Advance(TimeSpan.FromSeconds(105)); // t0+121 — nick cooldown истёк
        await d.TickAsync(CancellationToken.None);
        Assert.Equal(3, _store.LoadWake("delivered").Count);
    }

    [Fact]
    public void Subscribe_is_idempotent_and_unsubscribe_removes()
    {
        var d = Dispatcher(OkTransport());
        var s1 = d.Subscribe("Тень", "build_finished");
        var s2 = d.Subscribe("Тень", "build");
        Assert.NotNull(s1);
        Assert.NotNull(s2);
        Assert.Equal(s1!.Id, s2!.Id); // alias build → build_finished, дедуп
        Assert.Single(_store.LoadSubscriptions());

        Assert.Equal(1, d.Unsubscribe(null, "Тень", "build_finished"));
        Assert.Empty(_store.LoadSubscriptions());
    }

    [Fact]
    public void NotifyEvent_matches_subscription_and_enqueues()
    {
        var d = Dispatcher(OkTransport());
        _ = d.Subscribe("Тень", "build_finished", taskFilter: "dashspec");

        d.NotifyEvent("build", ok: true, pulse: "green", detail: "dashspec wave-2 build ok");

        var pending = _store.LoadWake("pending");
        Assert.Single(pending);
        Assert.Equal("Тень", pending[0].Nick);
        Assert.Contains("build_finished: ok", pending[0].Body);
    }

    [Fact]
    public async Task Queue_overflow_drops_oldest_pending()
    {
        var opts = new CdpHabitatOptions { MaxPending = 2, KeepCompleted = 10 };
        var d = Dispatcher(OkTransport(), opts);
        _ = d.Enqueue(CdpWakeDispatcher.KindLetter, "первый", nick: "A", session: "s1", harness: "opencode");
        _time.Advance(TimeSpan.FromSeconds(1));
        _ = d.Enqueue(CdpWakeDispatcher.KindLetter, "второй", nick: "B", session: "s2", harness: "opencode");
        _time.Advance(TimeSpan.FromSeconds(1));
        _ = d.Enqueue(CdpWakeDispatcher.KindLetter, "третий", nick: "C", session: "s3", harness: "opencode");

        var skipped = _store.LoadWake("skipped");
        Assert.Single(skipped);
        Assert.Equal("queue_overflow", skipped[0].SkippedReason);
        Assert.Equal(2, _store.LoadWake("pending").Count);
    }

    [Fact]
    public async Task Unknown_harness_fails_honestly()
    {
        var d = Dispatcher(OkTransport());
        _ = d.Enqueue(CdpWakeDispatcher.KindLetter, "привет", nick: "Кто-то", harness: "telepathy");

        await d.TickAsync(CancellationToken.None);

        var failed = _store.LoadWake("failed");
        Assert.Single(failed);
        Assert.Equal("unknown_harness", failed[0].SkippedReason);
    }

    sealed class FixedStateRoot(string root) : IStateRootProvider
    {
        public string ResolveStateRoot() => root;
    }

    sealed class MutableTime : TimeProvider
    {
        DateTimeOffset _utc = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan delta) => _utc += delta;

        public override DateTimeOffset GetUtcNow() => _utc;
    }
}