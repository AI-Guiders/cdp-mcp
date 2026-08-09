#nullable enable
using CdpMcp.IntentWorkspace;
using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;
using Xunit;

namespace CdpMcp.Tests;

public sealed class CitizenFocusLaneBindTests : IDisposable
{
    readonly string _root;

    public CitizenFocusLaneBindTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-face-lane-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CideIntercomIdentityLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        CideIntercomIdentityLatch.RootOverrideForTests = null;
        IdeIgniteArmHost.BindCitizenFocusLane(() => { });
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void TryApplyCitizenFocusLane_switches_to_Face_not_tip()
    {
        Assert.NotNull(CideIntercomIdentityLatch.Claim("pf", "Sierra", "citizen", "zai-org/GLM-5.1"));
        Assert.NotNull(CideIntercomIdentityLatch.Claim("pf", "Кир", "guest", "zai-org/GLM-5.1"));
        Assert.Equal("Кир", CideIntercomIdentityLatch.TrySeat("pf")?.Name);

        var path = Path.Combine(Path.GetTempPath(), "cdp-face-lane-db-" + Guid.NewGuid().ToString("N") + ".witdb");
        try
        {
            var store = BootStore(path);
            var state = new IntentWorkspaceState { DatabasePath = path, FocusLane = "Кир" };
            store.WorkFocusHydrate(state);
            Assert.Equal("Кир", state.FocusLane);

            IdeIgniteArmHost.BindCitizenFocusLane(() =>
            {
                var (who, _) = CitizenGlassDialogBridge.ResolveCitizenFace();
                store.WorkFocusSwitchLane(state, who);
            });
            IdeIgniteArmHost.TryApplyCitizenFocusLane();

            Assert.Equal("Sierra", state.FocusLane);
            Assert.Equal("Кир", CideIntercomIdentityLatch.TrySeat("pf")?.Name);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    static IntentWorkspaceStore BootStore(string path)
    {
        var opts = new DbContextOptionsBuilder<IntentWorkspaceDbContext>()
            .UseWitDb($"Data Source={path}")
            .Options;
        using (var boot = new IntentWorkspaceDbContext(opts))
            boot.Database.EnsureCreated();
        var store = new IntentWorkspaceStore(opts, path);
        store.EnsureWorkFocusTable();
        return store;
    }
}
