#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// Dual-cockpit Intercom cannon: human @PF latch → AutoIgnition CDT inject
/// ("Message for you, sir!"). Agent looks desk / wake charge — not peek JSON APIs.
/// </summary>
internal sealed class IntercomVoiceCannonWatcher : IDisposable
{
    readonly FileSystemWatcher _watcher;
    readonly object _gate = new();
    string? _lastFiredId;
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
        TryFireFromDisk(force: true);
    }

    public static IntercomVoiceCannonWatcher Start() =>
        new(CideIntercomVoiceLatch.StateRoot);

    void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        _ = Task.Run(() =>
        {
            Thread.Sleep(40);
            TryFireFromDisk(force: false);
        });
    }

    void TryFireFromDisk(bool force)
    {
        if (_disposed)
            return;

        var unread = CideIntercomVoiceLatch.TryUnreadForPf();
        if (unread is null)
            return;

        lock (_gate)
        {
            if (!force && string.Equals(unread.Id, _lastFiredId, StringComparison.OrdinalIgnoreCase))
                return;
            _lastFiredId = unread.Id;
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
            // charge=custom — otherwise arm would replace with canonical wake text.
            var args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["op"] = JsonSerializer.SerializeToElement("arm"),
                ["when"] = JsonSerializer.SerializeToElement("timer"),
                ["in"] = JsonSerializer.SerializeToElement("1s"),
                ["once"] = JsonSerializer.SerializeToElement(true),
                ["force"] = JsonSerializer.SerializeToElement(true),
                ["charge"] = JsonSerializer.SerializeToElement("custom"),
                ["message"] = JsonSerializer.SerializeToElement(message),
                ["id"] = JsonSerializer.SerializeToElement("intercom-pf-" + unread.Id)
            };
            _ = IdeIgniteChannel.HandleJson(args);
        }
        catch
        {
            /* best-effort cannon */
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _watcher.Dispose();
    }
}
