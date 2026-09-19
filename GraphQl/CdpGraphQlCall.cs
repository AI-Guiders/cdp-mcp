#nullable enable
using System.Text.Json;
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

    /// <summary>Optional domain dispatch (git / memory_world) — null in schema-warm tests.</summary>
    public Func<string, IReadOnlyDictionary<string, JsonElement>, CancellationToken, Task<string>>? DispatchToolAsync { get; init; }

    public static CdpGraphQlCall From(IResolverContext ctx) =>
        ctx.GetGlobalStateOrDefault<CdpGraphQlCall>(StateKey)
        ?? throw new GraphQLException("no_graphql_call: MetaDispatch must SetGlobalState(CdpGraphQlCall) before execute");
}