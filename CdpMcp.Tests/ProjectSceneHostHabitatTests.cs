using Cdp.ScriptableIde;
using Xunit;

namespace CdpMcp.Tests;

public sealed class ProjectSceneHostHabitatTests
{
    [Fact]
    public void IsHostHabitatRoot_true_for_local_app_data_root_only()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.True(ProjectScene.IsHostHabitatRoot(local));
        // Nested work dirs under Local (Temp, publish) are not habitat.
        Assert.False(ProjectScene.IsHostHabitatRoot(Path.Combine(local, "cdp-mcp")));
    }

    [Fact]
    public void IsHostHabitatRoot_true_for_application_data_junction_name()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var classic = Path.Combine(profile, "Application Data");
        Assert.True(ProjectScene.IsHostHabitatRoot(classic));
    }

    [Fact]
    public void IsHostHabitatRoot_false_for_temp_work_dir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-habitat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.False(ProjectScene.IsHostHabitatRoot(dir));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task SceneAsync_host_habitat_returns_curated_without_throw()
    {
        var bus = new ScriptToolBus { IsDryRun = false };
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var habitat = Path.Combine(profile, "Application Data");
        var plan = new PlanContext
        {
            PrimaryRoot = habitat,
            WorkRoot = habitat,
            PlanId = "",
            Language = "csharp"
        };

        var step = await ProjectOps.SceneAsync(bus, plan, root: null).ConfigureAwait(false);
        Assert.True(step.Ok);
        Assert.NotNull(step.Data);
        Assert.True(step.Data!.Value.TryGetProperty("scan_note", out var note));
        Assert.Contains("host habitat", note.GetString() ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.True(step.Data.Value.TryGetProperty("templates_curated", out var curated));
        Assert.True(curated.GetArrayLength() > 0);
        Assert.True(step.Data.Value.TryGetProperty("existing", out var existing));
        Assert.True(existing.TryGetProperty("projects", out var projects));
        Assert.Equal(0, projects.GetArrayLength());
    }
}
