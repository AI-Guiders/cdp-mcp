using System.Threading.Channels;
using Cdp.CdpState;
using NSubstitute;
using Xunit;

namespace CdpMcp.Tests;

/// <summary>
/// CDP-ADR-0227 — dsh-carrier доставка: DshWakeHub (in-memory SSE) + dispatcher case "dsh".
/// Хаб статический (process-local) — каждый тест регистрирует и снимает своих watcher'ов.
/// </summary>
public class DshWakeDeliveryTests : IDisposable
{
    readonly string _root;
    readonly ICdpStateStore _store;
    readonly IAgentRoster _roster = Substitute.For<IAgentRoster>();
    readonly CdpHabitatOptions _options = new() { WakeNickCooldownSeconds = 120, DefaultDeliveryCooldownSeconds = 15 };
    readonly List<IDisposable> _registrations = new();

    public DshWakeDeliveryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-dsh-wake-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
        _store = new WitDbCdpStateStore(new FixedStateRoot(_root));
    }

    public void Dispose()
    {
        foreach (var r in _registrations) r.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    CdpWakeDispatcher Dispatcher() => new(
        _roster,
        Substitute.For<IOpencodeWakeTransport>(),
        _store,
        stateRoot: new FixedStateRoot(_root),
        options: _options);

    (Channel<CdpWakeEnvelopeEntity>, IDisposable) Watch(string nick, string bridgeSession)
    {
        var channel = Channel.CreateUnbounded<CdpWakeEnvelopeEntity>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        var reg = DshWakeHub.Register(nick, bridgeSession, channel.Writer);
        _registrations.Add(reg);
        return (channel, reg);
    }

    [Fact]
    public async Task Dsh_letter_delivered_to_watcher()
    {
        var (channel, _) = Watch("Эхо", "b-1");
        var d = Dispatcher();
        _ = d.Enqueue(CdpWakeDispatcher.KindLetter, "привет", nick: "Эхо", harness: "dsh");

        await d.TickAsync(CancellationToken.None);

        Assert.True(channel.Reader.TryRead(out var e));
        Assert.Equal("привет", e.Body);
        var delivered = _store.LoadWake("delivered");
        Assert.Single(delivered);
        Assert.Equal("dsh", delivered[0].Detail);
        Assert.Empty(_store.LoadWake("pending"));
    }

    [Fact]
    public async Task Dsh_no_watcher_stays_pending()
    {
        var d = Dispatcher();
        _ = d.Enqueue(CdpWakeDispatcher.KindLetter, "привет", nick: "Эхо", harness: "dsh");

        await d.TickAsync(CancellationToken.None);

        var pending = _store.LoadWake("pending");
        Assert.Single(pending);
        Assert.Contains("no_dsh_watcher", pending[0].Detail);
        Assert.Empty(_store.LoadWake("delivered"));
    }

    [Fact]
    public async Task Dsh_session_targeting_reaches_only_matching_watcher()
    {
        var (a, _) = Watch("Эхо", "b-1");
        var (b, _) = Watch("Эхо", "b-2");
        var d = Dispatcher();
        _ = d.Enqueue(CdpWakeDispatcher.KindBuildFinished, "build ok", nick: "Эхо", session: "b-1", harness: "dsh");

        await d.TickAsync(CancellationToken.None);

        Assert.True(a.Reader.TryRead(out _));
        Assert.False(b.Reader.TryRead(out _));
        Assert.Single(_store.LoadWake("delivered"));
    }

    [Fact]
    public async Task Dsh_line_event_reaches_all_watchers()
    {
        var (a, _) = Watch("Эхо", "b-1");
        var (b, _) = Watch("Эхо", "b-2");
        var d = Dispatcher();
        _ = d.Enqueue(CdpWakeDispatcher.KindLetter, "всем", nick: "Эхо", harness: "dsh");

        await d.TickAsync(CancellationToken.None);

        Assert.True(a.Reader.TryRead(out _));
        Assert.True(b.Reader.TryRead(out _));
    }

    [Fact]
    public void PendingDsh_lists_only_own_nick()
    {
        var d = Dispatcher();
        _ = d.Enqueue(CdpWakeDispatcher.KindLetter, "эхо", nick: "Эхо", harness: "dsh");
        _ = d.Enqueue(CdpWakeDispatcher.KindLetter, "тень", nick: "Тень", harness: "dsh");

        var forEcho = d.PendingDsh("Эхо");
        Assert.Single(forEcho);
        Assert.Equal("эхо", forEcho[0].Body);
    }

    [Fact]
    public async Task Toast_envelope_delivered_to_watcher()
    {
        // CDP-ADR-0228: операторский тост — тот же hub, другой потребитель.
        var (channel, _) = Watch("оператор", "b-1");
        var d = Dispatcher();
        _ = d.Enqueue(CdpWakeDispatcher.KindPeerShip, "apply ok", nick: "оператор", harness: "toast");

        await d.TickAsync(CancellationToken.None);

        Assert.True(channel.Reader.TryRead(out var e));
        Assert.Equal("apply ok", e.Body);
        Assert.Single(_store.LoadWake("delivered"));
        Assert.Empty(_store.LoadWake("pending"));
    }

    sealed class FixedStateRoot(string root) : IStateRootProvider
    {
        public string ResolveStateRoot() => root;
    }
}
