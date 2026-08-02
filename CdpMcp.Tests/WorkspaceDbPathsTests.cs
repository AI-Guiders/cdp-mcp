#nullable enable
using Xunit;

namespace CdpMcp.Tests;

public sealed class WorkspaceDbPathsTests
{
    [Fact]
    public void Resolve_override_wins()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-wsdb-" + Guid.NewGuid().ToString("N"));
        var overridePath = Path.Combine(root, "custom.witdb");
        try
        {
            var got = WorkspaceDbPaths.Resolve(overridePath, root, "cdp");
            Assert.Equal(Path.GetFullPath(overridePath), got);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Resolve_seat_path_under_state_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-wsdb-" + Guid.NewGuid().ToString("N"));
        try
        {
            var got = WorkspaceDbPaths.Resolve(null, root, "cdp-debug");
            Assert.Equal(
                Path.Combine(root, "cdp-debug", WorkspaceDbPaths.FileName),
                got);
            Assert.False(File.Exists(got));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Primary_migrates_legacy_flat_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-wsdb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var legacy = WorkspaceDbPaths.LegacyPath(root);
        File.WriteAllText(legacy, "wit-stub");
        try
        {
            var got = WorkspaceDbPaths.Resolve(null, root, WorkspaceDbPaths.PrimarySeat);
            Assert.Equal(WorkspaceDbPaths.SeatPath(root, "cdp"), got);
            Assert.True(File.Exists(got));
            Assert.False(File.Exists(legacy));
            Assert.Equal("wit-stub", File.ReadAllText(got));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Sibling_does_not_steal_legacy()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-wsdb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var legacy = WorkspaceDbPaths.LegacyPath(root);
        File.WriteAllText(legacy, "keep");
        try
        {
            var got = WorkspaceDbPaths.Resolve(null, root, "cdp-debug");
            Assert.Equal(WorkspaceDbPaths.SeatPath(root, "cdp-debug"), got);
            Assert.False(File.Exists(got));
            Assert.True(File.Exists(legacy));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort */ }
        }
    }
}
