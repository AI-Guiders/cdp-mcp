using CdpMcp.Habitat;
using Xunit;

namespace CdpMcp.Tests;

public sealed class HabitatRuleChainTests
{
    [Fact]
    public void FirstMatch_returns_first_applicable_rule()
    {
        IRule<int, string>[] rules =
        [
            new EvenRule(),
            new OddRule(),
        ];

        Assert.Equal("even", RuleChain.FirstMatch(2, rules));
        Assert.Equal("odd", RuleChain.FirstMatch(3, rules));
    }

    [Fact]
    public void FirstMatch_throws_when_no_rule_applies()
    {
        IRule<int, string>[] rules = [new EvenRule()];
        Assert.Throws<InvalidOperationException>(() => RuleChain.FirstMatch(3, rules));
    }

    [Fact]
    public void Pipe_applies_decorators_in_order()
    {
        var value = RuleChain.Pipe(
            1,
            x => x + 1,
            x => x * 10);
        Assert.Equal(20, value);
    }

    sealed class EvenRule : IRule<int, string>
    {
        public bool Applies(int context) => context % 2 == 0;
        public string Select(int context) => "even";
    }

    sealed class OddRule : IRule<int, string>
    {
        public bool Applies(int context) => context % 2 != 0;
        public string Select(int context) => "odd";
    }
}
