#nullable enable

namespace CdpMcp.Tests;

/// <summary>
/// Isolates citizen host-execute tests from live habitat latches (SoftFL apply, scar ledger).
/// Without this, tests read %LocalAppData%/cdp-mcp/*-LATEST.json from dogfood and fail scar gates.
/// </summary>
public sealed class CitizenRouteHostLifecycleFixture : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "cdp-citizen-host-" + Guid.NewGuid().ToString("N"));

    public CitizenRouteHostLifecycleFixture()
    {
        Directory.CreateDirectory(_root);
        CitizenSoftFlLeaf.RootOverrideForTests = _root;
        CitizenScarLedger.RootOverrideForTests = _root;
        ExploreCorrLatch.EnabledOverrideForTests = false;
        CitizenSoftFlLeaf.ApplyArmedOverrideForTests = false;
        CitizenSoftFlLeaf.ResetForTests();
        CitizenScarLedger.ResetForTests();
    }

    public void Dispose()
    {
        CitizenSoftFlLeaf.ResetForTests();
        CitizenScarLedger.ResetForTests();
        CitizenSoftFlLeaf.RootOverrideForTests = null;
        CitizenScarLedger.RootOverrideForTests = null;
        CitizenSoftFlLeaf.ApplyArmedOverrideForTests = null;
        ExploreCorrLatch.EnabledOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* temp */ }
    }
}
