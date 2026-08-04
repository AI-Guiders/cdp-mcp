using Xunit;

namespace CdpMcp.Tests;

public sealed class IdePressureSealedCourseTests : IDisposable
{
    public void Dispose() => IdePressureChannel.SealedCourseOverrideForTests = null;

    [Fact]
    public void ExtractSealedCourse_reads_operator_priority_section()
    {
        var body = """
            ## operator_priority (SEALED — do not rewrite)
            1. Glass Done
            2. Citizen Done

            ## agent_state
            noise
            """;

        var course = IdePressureChannel.ExtractSealedCourse(body);
        Assert.NotNull(course);
        Assert.Contains("Glass Done", course, StringComparison.Ordinal);
        Assert.Contains("Citizen Done", course, StringComparison.Ordinal);
        Assert.DoesNotContain("agent_state", course, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractSealedCourse_null_when_missing()
    {
        Assert.Null(IdePressureChannel.ExtractSealedCourse("## next\nfly"));
        Assert.Null(IdePressureChannel.ExtractSealedCourse(null));
    }

    [Fact]
    public void Publish_stamps_sealed_course_onto_wake_latch()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-course-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        IdeIgniteWakeLatch.RootOverrideForTests = root;
        IdePressureChannel.SealedCourseOverrideForTests =
            "## operator_priority (SEALED)\n1. Glass Done\n2. Citizen Done";

        try
        {
            var doc = IdeIgniteWakeLatch.Publish(
                "arm-course", "Resume TM.", IdeIgniteWakeLatch.ChannelComposer);
            Assert.NotNull(doc);
            Assert.Contains("Glass Done", doc!.Course, StringComparison.Ordinal);

            var latch = IdeIgniteWakeLatch.TryRead();
            Assert.NotNull(latch);
            Assert.Contains("Citizen Done", latch!.Course, StringComparison.Ordinal);
        }
        finally
        {
            IdeIgniteWakeLatch.RootOverrideForTests = null;
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }
}
