using CdpMcp.Habitat;
using Xunit;

namespace CdpMcp.Tests;

public sealed class IdeIgniteChargeStalenessTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    public void ChargeMarkersStale_empty(string? charge, bool stale) =>
        Assert.Equal(stale, IdeIgniteChargeStaleness.ChargeMarkersStale(charge));

    [Fact]
    public void ChargeMarkersStale_rejects_forbidden_marker() =>
        Assert.True(IdeIgniteChargeStaleness.ChargeMarkersStale(
            "Resume task. Drive Glass Done first. joint course. твоё — твоё"));

    [Fact]
    public void ChargeMarkersStale_requires_joint_course_and_ownership() =>
        Assert.True(IdeIgniteChargeStaleness.ChargeMarkersStale("Resume without markers"));

    [Fact]
    public void ChargeMarkersStale_fresh_when_markers_present() =>
        Assert.False(IdeIgniteChargeStaleness.ChargeMarkersStale(
            "Resume. joint course axis. Subjectivity твоё — твоё patch queue"));

    [Fact]
    public void CourseMarkersStale_empty_latched_with_expected() =>
        Assert.True(IdeIgniteChargeStaleness.CourseMarkersStale(null, "## operator_priority"));

    [Fact]
    public void CourseMarkersStale_expected_empty_not_stale() =>
        Assert.False(IdeIgniteChargeStaleness.CourseMarkersStale("## operator_priority sealed", null));

    [Fact]
    public void AnyMatch_short_circuits_on_first_true_rule()
    {
        IRule<int, bool>[] rules =
        [
            new FalseRule(),
            new TrueRule(),
            new ThrowRule(),
        ];

        Assert.True(RuleChain.AnyMatch(1, rules));
    }

    sealed class FalseRule : IRule<int, bool>
    {
        public bool Applies(int context) => true;
        public bool Select(int context) => false;
    }

    sealed class TrueRule : IRule<int, bool>
    {
        public bool Applies(int context) => true;
        public bool Select(int context) => true;
    }

    sealed class ThrowRule : IRule<int, bool>
    {
        public bool Applies(int context) => throw new InvalidOperationException("must not run");
        public bool Select(int context) => true;
    }
}
