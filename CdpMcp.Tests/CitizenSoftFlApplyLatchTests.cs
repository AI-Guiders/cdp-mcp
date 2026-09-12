#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public sealed class CitizenSoftFlApplyLatchTests : IDisposable
{
    readonly string _root;

    public CitizenSoftFlApplyLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-softfl-latch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CitizenSoftFlApplyLatch.RootOverrideForTests = _root;
        CitizenSoftFlApplyLatch.ResetForTests();
    }

    public void Dispose()
    {
        CitizenSoftFlApplyLatch.ResetForTests();
        CitizenSoftFlApplyLatch.RootOverrideForTests = null;
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch { /* ignore */ }
    }

    [Fact]
    public void Reads_legacy_leaf_latch_file()
    {
        var legacy = Path.Combine(_root, CitizenSoftFlApplyLatch.LegacyLatchFileName);
        var json = """
            {
              "schema": "citizen_softfl_leaf/v0",
              "at_utc": "2026-09-01T00:00:00Z",
              "leaf": {
                "id": "legacy-scope",
                "path": "D:/repo/Foo.cs",
                "mutation": "do thing",
                "dod": "done"
              },
              "apply_armed": true
            }
            """;
        File.WriteAllText(legacy, json);
        CitizenSoftFlApplyLatch.ReloadFromDiskForTests();

        Assert.Equal("legacy-scope", CitizenSoftFlApplyLatch.Current.Id);
        Assert.True(CitizenSoftFlApplyLatch.IsApplyArmed);
        Assert.True(CitizenSoftFlApplyLatch.HasPersistedScope);
    }

    [Fact]
    public void Persist_writes_v1_latch_and_drops_legacy()
    {
        CitizenSoftFlApplyLatch.Seed(new CitizenSoftFlApplyLatch.ApplyScope(
            "v1-scope", "D:/repo/Bar.cs", "mut", "dod"));
        CitizenSoftFlApplyLatch.ArmApply();

        Assert.True(File.Exists(CitizenSoftFlApplyLatch.LatchPath));
        Assert.False(File.Exists(Path.Combine(_root, CitizenSoftFlApplyLatch.LegacyLatchFileName)));

        using var doc = JsonDocument.Parse(File.ReadAllText(CitizenSoftFlApplyLatch.LatchPath));
        Assert.Equal(CitizenSoftFlApplyLatch.SchemaV1, doc.RootElement.GetProperty("schema").GetString());
        Assert.Equal("v1-scope", doc.RootElement.GetProperty("scope").GetProperty("id").GetString());
    }
}
