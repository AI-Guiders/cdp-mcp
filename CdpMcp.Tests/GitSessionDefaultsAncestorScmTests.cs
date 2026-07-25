using Xunit;

namespace CdpMcp.Tests;

public sealed class GitSessionDefaultsAncestorScmTests
{
    [Fact]
    public void DescribeAncestorScmRisk_null_when_same_root()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-scm-same-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Null(GitSessionDefaults.DescribeAncestorScmRisk(dir, dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void DescribeAncestorScmRisk_flags_when_scm_is_parent()
    {
        var parent = Path.Combine(Path.GetTempPath(), "cdp-scm-parent-" + Guid.NewGuid().ToString("N"));
        var child = Path.Combine(parent, "proj");
        Directory.CreateDirectory(child);
        try
        {
            var note = GitSessionDefaults.DescribeAncestorScmRisk(child, parent);
            Assert.NotNull(note);
            Assert.Contains("ancestor", note, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void DescribeAncestorScmRisk_null_when_unrelated()
    {
        var a = Path.Combine(Path.GetTempPath(), "cdp-scm-a-" + Guid.NewGuid().ToString("N"));
        var b = Path.Combine(Path.GetTempPath(), "cdp-scm-b-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(a);
        Directory.CreateDirectory(b);
        try
        {
            Assert.Null(GitSessionDefaults.DescribeAncestorScmRisk(a, b));
        }
        finally
        {
            Directory.Delete(a, recursive: true);
            Directory.Delete(b, recursive: true);
        }
    }
}
