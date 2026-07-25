using Cdp.ScriptableIde;
using Xunit;

namespace CdpMcp.Tests;

public sealed class ProjectScaffoldHygieneTests
{
    [Fact]
    public void TryPromoteClass1_renames_file_and_type()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-scaffold-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Class1.cs"), """
                namespace Demo;

                public class Class1
                {
                }
                """);

            var dest = ProjectScaffoldHygiene.TryPromoteClass1(dir, "DemoLib");
            Assert.NotNull(dest);
            Assert.True(File.Exists(dest));
            Assert.False(File.Exists(Path.Combine(dir, "Class1.cs")));
            var text = File.ReadAllText(dest!);
            Assert.Contains("class DemoLib", text, StringComparison.Ordinal);
            Assert.DoesNotContain("class Class1", text, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Theory]
    [InlineData("CdpAgentLab", "CdpAgentLab")]
    [InlineData("my-lib", "my_lib")]
    [InlineData("123Bad", "_123Bad")]
    public void SanitizeTypeName_is_identifier_safe(string input, string expected)
        => Assert.Equal(expected, ProjectScaffoldHygiene.SanitizeTypeName(input));
}
