#nullable enable
namespace CdpMcp.Cockpit.DataBus;

/// <summary>Sync in-process <see cref="IDataBus"/> (thin desk slice; no Avalonia).</summary>
public sealed class InMemoryDataBus : IDataBus, IDisposable
{
    readonly object _sync = new();
    readonly Dictionary<Type, List<Delegate>> _handlers = new();

    public void Publish<TEvent>(TEvent evt)
    {
        Delegate[] snapshot;
        lock (_sync)
        {
            if (!_handlers.TryGetValue(typeof(TEvent), out var list) || list.Count == 0)
                return;
            snapshot = list.ToArray();
        }

        foreach (var del in snapshot)
        {
            if (del is not Action<TEvent> handler)
                continue;
            try
            {
                handler(evt);
            }
            catch
            {
                // Isolate subscribers.
            }
        }
    }

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_sync)
        {
            if (!_handlers.TryGetValue(typeof(TEvent), out var list))
            {
                list = [];
                _handlers[typeof(TEvent)] = list;
            }

            list.Add(handler);
        }

        return new Subscription(() => Unsubscribe(handler));
    }

    public void Dispose()
    {
        lock (_sync)
            _handlers.Clear();
    }

    void Unsubscribe<TEvent>(Action<TEvent> handler)
    {
        lock (_sync)
        {
            if (!_handlers.TryGetValue(typeof(TEvent), out var list))
                return;
            list.Remove(handler);
            if (list.Count == 0)
                _handlers.Remove(typeof(TEvent));
        }
    }

    sealed class Subscription(Action unsubscribe) : IDisposable
    {
        Action? _unsubscribe = unsubscribe;

        public void Dispose() => Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
    }
}
