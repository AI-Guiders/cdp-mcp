#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// Dual-cockpit Intercom cannon: human @PF latch → AutoIgnition CDT inject
/// ("Message for you, sir!"). One wake per msgId (persisted across remount).
/// Agent looks desk / wake charge — not peek JSON APIs.
/// </summary>
internal sealed class IntercomVoiceCannonWatcher : IDisposable
{
    readonly FileSystemWatcher _watcher;
    readonly object _gate = new();
    bool _disposed;

    IntercomVoiceCannonWatcher(string stateRoot)
    {
        Directory.CreateDirectory(stateRoot);
        _watcher = new FileSystemWatcher(stateRoot)
        {
            Filter = "intercom-LATEST.json",
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnFsEvent;
        _watcher.Created += OnFsEvent;
        _watcher.Renamed += OnFsEvent;
        TryFireFromDisk();
    }

    public static IntercomVoiceCannonWatcher Start() =>
        new(CideIntercomVoiceLatch.StateRoot);

    void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        _ = Task.Run(() =>
        {
            Thread.Sleep(40);
            TryFireFromDisk();
        });
    }

    void TryFireFromDisk()
    {
        if (_disposed)
            return;

        var unread = CideIntercomVoiceLatch.TryUnreadForPf();
        if (unread is null)
            return;

        var msgId = unread.Id;
        var armId = IntercomVoiceCannonState.ArmIdFor(msgId);

        lock (_gate)
        {
            // Persistent + in-process: claim msgId before arming (survives remount).
            if (IntercomVoiceCannonState.WasFired(msgId))
                return;
            if (ArmAlreadyLive(armId))
            {
                _ = IntercomVoiceCannonState.TryMarkFired(msgId);
                return;
            }
            if (!IntercomVoiceCannonState.TryMarkFired(msgId))
                return;
        }

        var body = unread.Body.Trim();
        if (body.Length > 400)
            body = body[..397] + "…";

        var message =
            "Message for you, sir! @PM: " + body + "\n" +
            "---\n" +
            "Dual-cockpit Intercom. Habitat=CDP. " +
            "Read via cdp_intercom op=scene (or cockpit pulse), then op=ack. " +
            "Reply with cdp_intercom op=send to=@PM body=…";

        try
        {
            // No force= — same arm id must not replace/re-fire after once hygiene.
            // charge=custom — otherwise arm would replace with canonical wake text.
            var args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["op"] = JsonSerializer.SerializeToElement("arm"),
                ["when"] = JsonSerializer.SerializeToElement("timer"),
                ["in"] = JsonSerializer.SerializeToElement("1s"),
                ["once"] = JsonSerializer.SerializeToElement(true),
                ["charge"] = JsonSerializer.SerializeToElement("custom"),
                ["message"] = JsonSerializer.SerializeToElement(message),
                ["id"] = JsonSerializer.SerializeToElement(armId)
            };
            _ = IdeIgniteChannel.HandleJson(args);
        }
        catch
        {
            /* best-effort cannon */
        }
    }

    static bool ArmAlreadyLive(string armId) =>
        IdeIgniteArmHost.Snapshot().Any(a =>
            a.Id.Equals(armId, StringComparison.OrdinalIgnoreCase)
            && a.Status is "armed" or "firing");

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= OnFsEvent;
        _watcher.Created -= OnFsEvent;
        _watcher.Renamed -= OnFsEvent;
        _watcher.Dispose();
    }
}
