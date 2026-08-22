#nullable enable

namespace CdpMcp.Habitat;

/// <summary>
/// Idiomatic habitat primitives — ordered rules + post-select pipes. Not Platform; promote when N≥2 products reuse.
/// </summary>
internal interface IRule<in TContext, out TResult>
{
    bool Applies(TContext context);
    TResult Select(TContext context);
}

internal static class RuleChain
{
    /// <summary>First matching rule wins (Chain of Responsibility without MediatR).</summary>
    public static TResult FirstMatch<TContext, TResult>(
        TContext context,
        ReadOnlySpan<IRule<TContext, TResult>> rules)
    {
        foreach (var rule in rules)
        {
            if (!rule.Applies(context))
                continue;
            return rule.Select(context);
        }

        throw new InvalidOperationException("rule chain fell through");
    }

    /// <summary>Post-select decorator stack (policy wrap after FirstMatch).</summary>
    public static T Pipe<T>(T value, params Func<T, T>[] decorators)
    {
        foreach (var decorate in decorators)
            value = decorate(value);
        return value;
    }
}
