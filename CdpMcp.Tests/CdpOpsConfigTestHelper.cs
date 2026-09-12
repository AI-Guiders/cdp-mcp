#nullable enable
using Cdp.Config;

namespace CdpMcp.Tests;

internal static class CdpOpsConfigTestHelper
{
    public static IDisposable Bind(Action<CdpConfigDocument> configure)
    {
        var prev = CdpOpsConfig.Current;
        var doc = new CdpConfigDocument();
        configure(doc);
        CdpOpsConfig.Bind(doc);
        return new Restore(prev);
    }

    public static IDisposable BindTools(string? pluginsRoot = null, string? openvsxBase = null) =>
        Bind(doc =>
        {
            doc.Tools = new CdpConfigToolsSection
            {
                PluginsRoot = pluginsRoot,
                OpenvsxBase = openvsxBase
            };
        });

    public static IDisposable BindOps(bool? oomWakeCdtEdge = null) =>
        Bind(doc => doc.Ops = new CdpConfigOpsSection { OomWakeCdtEdge = oomWakeCdtEdge });

    sealed class Restore(CdpOpsConfig prev) : IDisposable
    {
        public void Dispose() => CdpOpsConfig.RestoreForTests(prev);
    }
}
