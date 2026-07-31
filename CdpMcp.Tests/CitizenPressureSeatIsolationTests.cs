#nullable enable
using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CdpProfileIsolation")]
public sealed class CitizenPressureSeatIsolationTests
{
    [Fact]
    public void Pressure_paths_include_install_seat_under_state_root()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CDP_PROFILE") ?? "default",
                "default",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var iso = $"D:\\tmp\\cdp-pressure-seat-{Guid.NewGuid():N}";
        CdpProfile.ApplyClientRoots([iso]);
        try
        {
            var seat = IdeIgniteArmHost.Seat;
            Assert.False(string.IsNullOrWhiteSpace(seat));
            Assert.Contains(Path.DirectorySeparatorChar + seat + Path.DirectorySeparatorChar,
                IdePressureChannel.FilePath,
                StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(CdpProfile.StateRoot, IdePressureChannel.SeatStateDir, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(
                Path.Combine(IdePressureChannel.SeatStateDir, "pressure-stash.json"),
                IdePressureChannel.FilePath);
            Assert.Equal(
                Path.Combine(CdpProfile.StateRoot, "pressure-stash.json"),
                IdePressureChannel.LegacyFilePath);

            Directory.CreateDirectory(CdpProfile.StateRoot);
            File.WriteAllText(IdePressureChannel.LegacyFilePath, """
                {"schema":"pressure_channel/v1","armed":true,"body":"migrated"}
                """.Trim());

            var session = new SessionContext { ProjectRoot = iso, Phase = CdpPhase.Explore, Object = CdpObjectKind.Code };
            _ = IdePressureChannel.HandleJson(session, Dict("op", "recall"));
            Assert.True(File.Exists(IdePressureChannel.FilePath));
            Assert.Contains("migrated", File.ReadAllText(IdePressureChannel.FilePath), StringComparison.Ordinal);
        }
        finally
        {
            CdpProfile.ApplyClientRoots(["D:\\tmp\\cdp-pressure-seat-cleanup"]);
            try { Directory.Delete(iso, recursive: true); } catch { /* best-effort */ }
        }
    }

    static Dictionary<string, JsonElement> Dict(params string[] kv)
    {
        var d = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        for (var i = 0; i + 1 < kv.Length; i += 2)
            d[kv[i]] = JsonSerializer.SerializeToElement(kv[i + 1]);
        return d;
    }
}
