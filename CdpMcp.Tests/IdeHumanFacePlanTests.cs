using Xunit;

namespace CdpMcp.Tests;

public class IdeHumanFacePlanTests
{
    [Fact]
    public void WhyLine_canonical_prefers_Glass_Done()
    {
        var why = IdeHumanFacePlan.WhyLine(IdePressureChannel.CanonicalSealedCourse);
        Assert.Equal("Glass Done (human flight)", why);
        Assert.False(IdeHumanFacePlan.LooksLikeAgentJargon(why!));
    }

    [Fact]
    public void WhyLine_skips_SoftFL_operator_eyes_theatre()
    {
        var course = """
            ## operator_priority (SEALED)
            SoftFL invent REJECT · tip mill ≠ Done · Face SoftOrgan/#CIDE Done needs operator eyes · refuse board hygiene
            1. Glass Done (human flight)
            2. Citizen Done stable → 15.08
            Before act:
            - Viewer? human eyes vs agent text
            """;
        var why = IdeHumanFacePlan.WhyLine(course);
        Assert.NotNull(why);
        Assert.DoesNotContain("SoftFL", why!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("operator eyes", why, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tip mill", why, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Glass Done", why, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WhyLine_softfl_only_course_falls_back_to_human_goal()
    {
        var course = """
            ## operator_priority (SEALED)
            SoftFL invent REJECT · tip mill ≠ Done · needs operator eyes
            Autonomous ON · Habitat=CDP
            """;
        var why = IdeHumanFacePlan.WhyLine(course);
        Assert.NotNull(why);
        Assert.DoesNotContain("SoftFL", why!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("operator eyes", why, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NextLeaf_strips_invent_only_hold_prefix()
    {
        var next = IdeHumanFacePlan.NextLeaf(
            "Sat-eve DoD invent-only Hold — Sierra KB+net+SA SoftOrgan+IDE lived @act #CDP");
        Assert.DoesNotContain("SoftFL", next, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Sierra", next, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@act", next, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PulseLine_strips_ShowFace_SoftFL_mill()
    {
        var pulse = IdeHumanFacePlan.PulseLine(
            "wave · open · 0/1 · ShowFace Place+attention SoftFL… · local пт 2026-08-07");
        Assert.DoesNotContain("SoftFL", pulse, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ShowFace", pulse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("wave", pulse, StringComparison.OrdinalIgnoreCase);
    }
}
