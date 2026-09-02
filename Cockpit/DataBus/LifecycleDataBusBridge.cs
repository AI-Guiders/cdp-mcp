#nullable enable

using AIGuiders.Platform.Cockpit.DataBus;
using CdpMcp.Cockpit.DataBus;

namespace CdpMcp.Cockpit.DataBus;

/// <summary>Publish lifecycle build/test state into desk DataBus for IdeHealth CCU.</summary>
internal static class LifecycleDataBusBridge
{
    public static async Task<string> WithBuildStateAsync(Func<Task<string>> run)
    {
        Publish(new BuildStateChanged(true));
        try
        {
            var result = await run().ConfigureAwait(false);
            var ok = LifecycleResultParser.LooksOk(result);
            Publish(new BuildStateChanged(false, ok ? 0 : 1, ok));
            return result;
        }
        catch
        {
            Publish(new BuildStateChanged(false, 1, false));
            throw;
        }
    }

    public static async Task<string> WithTestStateAsync(Func<Task<string>> run)
    {
        Publish(new BuildStateChanged(true));
        try
        {
            var result = await run().ConfigureAwait(false);
            var ok = LifecycleResultParser.LooksOk(result);
            Publish(new BuildStateChanged(false, ok ? 0 : 1, ok));
            if (ok)
                Publish(new TestsStateChanged("ok", 0));
            return result;
        }
        catch
        {
            Publish(new BuildStateChanged(false, 1, false));
            throw;
        }
    }

    static void Publish<T>(T evt)
    {
        try
        {
            DeskDataBusHost.Current.Publish(evt);
        }
        catch
        {
            /* bus optional in tests */
        }
    }
}

internal static class LifecycleResultParser
{
    public static bool LooksOk(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var ok))
                return ok.ValueKind == System.Text.Json.JsonValueKind.True;
            if (root.TryGetProperty("success", out var success))
                return success.ValueKind == System.Text.Json.JsonValueKind.True;
            if (root.TryGetProperty("exit_code", out var code) && code.TryGetInt32(out var n))
                return n == 0;
            if (root.TryGetProperty("error_count", out var ec) && ec.TryGetInt32(out var errors))
                return errors == 0;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
