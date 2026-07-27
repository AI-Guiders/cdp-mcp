#nullable enable
using Xunit;

namespace CdpMcp.Tests;

public class CdpProfileTests
{
    [Theory]
    [InlineData("file:///D:/Experiments/Foo", "D:\\Experiments\\Foo")]
    [InlineData("D:\\Experiments\\Bar", "D:\\Experiments\\Bar")]
    public void UriToPath_normalizes_file_uris(string input, string expected)
    {
        var path = CdpProfile.UriToPath(input);
        Assert.NotNull(path);
        Assert.Equal(Path.GetFullPath(expected), Path.GetFullPath(path!));
    }

    [Fact]
    public void HashKey_is_stable_and_order_independent_via_NormalizePaths()
    {
        var a = CdpProfile.NormalizePaths(["D:\\A", "D:\\B"]);
        var b = CdpProfile.NormalizePaths(["D:\\B", "D:\\A"]);
        Assert.Equal(CdpProfile.HashKey(a), CdpProfile.HashKey(b));
        Assert.Equal(12, CdpProfile.HashKey(a).Length);
    }

    [Fact]
    public void ApplyClientRoots_switches_state_root_when_env_default()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CDP_PROFILE") ?? "default",
                "default",
                StringComparison.OrdinalIgnoreCase))
        {
            return; // env override active in this process
        }

        var before = CdpProfile.StateRoot;
        var changed = CdpProfile.ApplyClientRoots(["D:\\tmp\\cdp-iso-test-a", "D:\\tmp\\cdp-iso-test-b"]);
        Assert.True(changed || CdpProfile.Kind == "client_roots");
        Assert.Equal("client_roots", CdpProfile.Kind);
        Assert.Contains(Path.Combine("cdp-mcp", "ws"), CdpProfile.StateRoot, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(before, CdpProfile.StateRoot);

        // Different workspace → different hash folder
        var rootA = CdpProfile.StateRoot;
        CdpProfile.ApplyClientRoots(["D:\\tmp\\cdp-iso-test-other"]);
        Assert.NotEqual(rootA, CdpProfile.StateRoot);
    }
}
