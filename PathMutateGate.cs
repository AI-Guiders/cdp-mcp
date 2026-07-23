using System.Collections.Concurrent;

namespace CdpMcp;

/// <summary>
/// Per-path single-flight for mutate. Different paths stay concurrent;
/// same path serializes so parallel agent tool calls do not tear the file.
/// Agent keeps firing N tools — harness absorbs the race.
/// </summary>
internal sealed class PathMutateGate
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks =
        new(StringComparer.OrdinalIgnoreCase);

    SemaphoreSlim For(string path)
    {
        var key = Path.GetFullPath(path);
        return _locks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
    }

    public void Run(string path, Action action)
    {
        var gate = For(path);
        gate.Wait();
        try
        {
            action();
        }
        finally
        {
            gate.Release();
        }
    }

    public T Run<T>(string path, Func<T> action)
    {
        var gate = For(path);
        gate.Wait();
        try
        {
            return action();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task RunAsync(string path, Func<Task> action, CancellationToken cancellationToken = default)
    {
        var gate = For(path);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await action().ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<T> RunAsync<T>(string path, Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        var gate = For(path);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }
}
