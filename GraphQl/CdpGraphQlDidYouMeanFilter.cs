#nullable enable
using HotChocolate;
using HotChocolate.Execution;

namespace CdpMcp.GraphQl;

/// <summary>HC adapter — delegates to <see cref="GraphQlUnknownFieldErgonomics"/>.</summary>
internal sealed class CdpGraphQlDidYouMeanFilter : IErrorFilter
{
    public IError OnError(IError error) => GraphQlUnknownFieldErgonomics.ApplyToError(error);
}
