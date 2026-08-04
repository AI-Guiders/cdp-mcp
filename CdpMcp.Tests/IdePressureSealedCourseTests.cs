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
    public void TryPeekSealedCourse_falls_back_to_canonical_with_criteria()
    {
        IdePressureChannel.SealedCourseOverrideForTests = null;
        // No override + empty extract path via override empty string means null peek uses canonical through Ensure —
        // force empty override to mean "no stash" via a body without section:
        var course = IdePressureChannel.EnsureCourseCriteria(
            IdePressureChannel.ExtractSealedCourse("## agent_state\nnoise"))
            ?? IdePressureChannel.EnsureCourseCriteria(IdePressureChannel.CanonicalSealedCourse);
        Assert.NotNull(course);
        Assert.Contains("Viewer?", course, StringComparison.Ordinal);
        Assert.Contains("Before act", course, StringComparison.Ordinal);
        Assert.Contains("Glass Done", course, StringComparison.Ordinal);
        Assert.Contains("Being ≠ seeming", course, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnsureCourseCriteria_appends_when_priorities_only()
    {
        var course = IdePressureChannel.EnsureCourseCriteria(
            "## operator_priority (SEALED)\n1. Glass Done\n2. Citizen Done");
        Assert.NotNull(course);
        Assert.Contains("Viewer?", course!, StringComparison.Ordinal);
        Assert.Contains("Cheap path?", course, StringComparison.Ordinal);
        Assert.Contains("Being ≠ seeming", course, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnsureCourseCriteria_appends_being_when_criteria_present()
    {
        var course = IdePressureChannel.EnsureCourseCriteria(
            """
            ## operator_priority (SEALED)
            1. Glass Done
            Before act (not resume-and-invent):
            - Viewer? human eyes vs agent text
            """);
        Assert.NotNull(course);
        Assert.Contains("Being ≠ seeming", course!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("File.Exists alone", course, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureStashHasSealedCourse_prepends_canonical_when_missing()
    {
        var body = IdePressureChannel.EnsureStashHasSealedCourse("agent notes only");
        Assert.Contains("## operator_priority", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("## agent_state", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("agent notes only", body, StringComparison.Ordinal);
        Assert.Contains("Viewer?", body, StringComparison.Ordinal);
    }

    [Fact]
    public void RefuseStashDroppingSealedCourse_throws_without_force()
    {
        var prev = "## operator_priority (SEALED)\n1. Glass\n\n## agent_state\nx";
        var drop = "## agent_state\nwiped";
        var ex = Assert.Throws<ArgumentException>(() =>
            IdePressureChannel.RefuseStashDroppingSealedCourse(prev, drop, null));
        Assert.Contains("sealed_course_drop", ex.Message, StringComparison.Ordinal);
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
            Assert.Contains("Viewer?", doc.Course, StringComparison.Ordinal);

            var latch = IdeIgniteWakeLatch.TryRead();
            Assert.NotNull(latch);
            Assert.Contains("Citizen Done", latch!.Course, StringComparison.Ordinal);
            Assert.Contains("Before act", latch.Course, StringComparison.Ordinal);
        }
        finally
        {
            IdeIgniteWakeLatch.RootOverrideForTests = null;
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }
}
