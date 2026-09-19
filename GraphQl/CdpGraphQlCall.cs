#nullable enable
using Cdp.Core;
using Cdp.ScriptableIde;
using HotChocolate;
using HotChocolate.Resolvers;

namespace CdpMcp.GraphQl;

/// <summary>
/// Per-request GraphQL host — MetaDispatch tenant Session/DocStore (ADR-0200),
/// not singleton CdpHostRuntime.Session (default slice may be empty).
/// </summary>
internal sealed class CdpGraphQlCall
{
    public const string StateKey = "cdp.graphql.call";

    public required SessionContext Session { get; init; }
    public required DocumentBufferStore DocStore { get; init; }
    public required CdpSettings Settings { get; init; }

    public static CdpGraphQlCall From(IResolverContext ctx) =>
        ctx.GetGlobalStateOrDefault<CdpGraphQlCall>(StateKey)
        ?? throw new GraphQLException("no_graphql_call: MetaDispatch must SetGlobalState(CdpGraphQlCall) before execute");
}
