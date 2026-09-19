#nullable enable
using HotChocolate.Types;

namespace CdpMcp.GraphQl;

/// <summary>
/// Explicit Query ObjectType — avoids convention discovery on CdpQueryRoot
/// ([Service] CdpHostRuntime / internal root → TS_UNRESOLVED_TYPES on HC 15/16).
/// </summary>
internal sealed class CdpQueryType : ObjectType
{
    protected override void Configure(IObjectTypeDescriptor descriptor)
    {
        descriptor.Name("Query");

        descriptor
            .Field("textHits")
            .Argument("query", a => a.Type<StringType>())
            .Argument("path", a => a.Type<StringType>())
            .Argument("scope", a => a.Type<StringType>())
            .Argument("glob", a => a.Type<StringType>())
            .Argument("regex", a => a.Type<BooleanType>())
            .Argument("ignoreCase", a => a.Type<BooleanType>())
            .Argument("like", a => a.Type<StringType>())
            .Argument("first", a => a.Type<IntType>().DefaultValue(20))
            .Type<ObjectType<TextHitConnection>>()
            .Resolve(ctx =>
            {
                var call = CdpGraphQlCall.From(ctx);
                return new CdpQueryRoot().TextHits(
                    call,
                    ctx.ArgumentValue<string?>("query"),
                    ctx.ArgumentValue<string?>("path"),
                    ctx.ArgumentValue<string?>("scope"),
                    ctx.ArgumentValue<string?>("glob"),
                    ctx.ArgumentValue<bool?>("regex"),
                    ctx.ArgumentValue<bool?>("ignoreCase"),
                    ctx.ArgumentValue<string?>("like"),
                    ctx.ArgumentValue<int>("first"));
            });

        descriptor
            .Field("peek")
            .Argument("path", a => a.Type<NonNullType<StringType>>())
            .Argument("offset", a => a.Type<IntType>())
            .Argument("limit", a => a.Type<IntType>())
            .Type<ObjectType<PeekResult>>()
            .Resolve(ctx =>
            {
                var call = CdpGraphQlCall.From(ctx);
                return new CdpQueryRoot().Peek(
                    call,
                    ctx.ArgumentValue<string>("path"),
                    ctx.ArgumentValue<int?>("offset"),
                    ctx.ArgumentValue<int?>("limit"));
            });

        descriptor
            .Field("diagnostics")
            .Argument("path", a => a.Type<StringType>())
            .Argument("first", a => a.Type<IntType>().DefaultValue(50))
            .Type<ListType<ObjectType<DiagnosticNode>>>()
            .Resolve(ctx =>
            {
                var call = CdpGraphQlCall.From(ctx);
                return new CdpQueryRoot().Diagnostics(
                    call,
                    ctx.ArgumentValue<string?>("path"),
                    ctx.ArgumentValue<int>("first"));
            });

        descriptor
            .Field("goto")
            .Argument("query", a => a.Type<NonNullType<StringType>>())
            .Argument("kind", a => a.Type<StringType>())
            .Argument("first", a => a.Type<IntType>().DefaultValue(20))
            .Type<ListType<ObjectType<GotoHitNode>>>()
            .Resolve(ctx =>
            {
                var call = CdpGraphQlCall.From(ctx);
                return new CdpQueryRoot().Goto(
                    call,
                    ctx.ArgumentValue<string>("query"),
                    ctx.ArgumentValue<string?>("kind"),
                    ctx.ArgumentValue<int>("first"));
            });
        descriptor
            .Field("session")
            .Type<ObjectType<SessionNode>>()
            .Resolve(ctx =>
            {
                var call = CdpGraphQlCall.From(ctx);
                return new CdpQueryRoot().Session(call);
            });

        descriptor
            .Field("correspondence")
            .Argument("path", a => a.Type<StringType>())
            .Argument("anchor", a => a.Type<StringType>())
            .Argument("slim", a => a.Type<BooleanType>().DefaultValue(true))
            .Type<ObjectType<CorrespondenceResult>>()
            .Resolve(ctx =>
            {
                var call = CdpGraphQlCall.From(ctx);
                return new CdpQueryRoot().Correspondence(
                    call,
                    ctx.ArgumentValue<string?>("path"),
                    ctx.ArgumentValue<string?>("anchor"),
                    ctx.ArgumentValue<bool>("slim"));
            });

        descriptor
            .Field("git")
            .Type<GitReadQueryType>()
            .Resolve(ctx => new GitReadQuery(CdpGraphQlCall.From(ctx)));

        descriptor
            .Field("knowledge")
            .Type<KnowledgeReadQueryType>()
            .Resolve(ctx => new KnowledgeReadQuery(CdpGraphQlCall.From(ctx)));

        descriptor
            .Field("semanticMap")
            .Argument("path", a => a.Type<StringType>())
            .Argument("anchor", a => a.Type<StringType>())
            .Argument("mode", a => a.Type<StringType>())
            .Argument("maxRelated", a => a.Type<IntType>())
            .Type<ObjectType<EngineEnvelopeNode>>()
            .Resolve(async ctx =>
            {
                var call = CdpGraphQlCall.From(ctx);
                return await SceneReadResolvers.SemanticMapAsync(
                    call,
                    ctx.ArgumentValue<string?>("path"),
                    ctx.ArgumentValue<string?>("anchor"),
                    ctx.ArgumentValue<string?>("mode"),
                    ctx.ArgumentValue<int?>("maxRelated"),
                    ctx.RequestAborted).ConfigureAwait(false);
            });

        descriptor
            .Field("codeClones")
            .Argument("path", a => a.Type<StringType>())
            .Type<ObjectType<EngineEnvelopeNode>>()
            .Resolve(async ctx =>
            {
                var call = CdpGraphQlCall.From(ctx);
                return await SceneReadResolvers.CodeClonesAsync(
                    call,
                    ctx.ArgumentValue<string?>("path"),
                    ctx.RequestAborted).ConfigureAwait(false);
            });

        descriptor
            .Field("symbol")
            .Argument("name", a => a.Type<NonNullType<StringType>>())
            .Argument("file", a => a.Type<StringType>())
            .Type<SymbolReadResultType>()
            .Resolve(async ctx =>
            {
                var call = CdpGraphQlCall.From(ctx);
                return await SymbolReadResolvers.SymbolAsync(
                    call,
                    ctx.ArgumentValue<string>("name"),
                    ctx.ArgumentValue<string?>("file"),
                    ctx.RequestAborted).ConfigureAwait(false);
            });

        descriptor
            .Field("testScene")
            .Type<ObjectType<EngineEnvelopeNode>>()
            .Resolve(async ctx =>
            {
                var call = CdpGraphQlCall.From(ctx);
                return await SceneReadResolvers.TestSceneAsync(call, ctx.RequestAborted).ConfigureAwait(false);
            });

        descriptor
            .Field("packages")
            .Type<PackagesReadQueryType>()
            .Resolve(ctx => new PackagesReadQuery(CdpGraphQlCall.From(ctx)));

        // Tier B situational pulses (execute verbs stay MCP)
        WireEnvelope(descriptor, "health", SituationalReadResolvers.HealthAsync);
        WireEnvelope(descriptor, "recent", SituationalReadResolvers.RecentAsync);
        WireEnvelope(descriptor, "lifecycle", SituationalReadResolvers.LifecycleAsync);
        WireEnvelope(descriptor, "projectScene", SituationalReadResolvers.ProjectSceneAsync);
        WireEnvelope(descriptor, "editorScene", SituationalReadResolvers.EditorSceneAsync);
        WireEnvelope(descriptor, "shellScene", SituationalReadResolvers.ShellSceneAsync);
        WireEnvelope(descriptor, "canonStack", SituationalReadResolvers.CanonStackAsync);
        WireEnvelope(descriptor, "lastBuild", SituationalReadResolvers.LastBuildAsync);
        WireEnvelope(descriptor, "lastTest", SituationalReadResolvers.LastTestAsync);
    }

    static void WireEnvelope(
        IObjectTypeDescriptor descriptor,
        string name,
        Func<CdpGraphQlCall, CancellationToken, Task<EngineEnvelopeNode>> resolve)
    {
        descriptor
            .Field(name)
            .Type<ObjectType<EngineEnvelopeNode>>()
            .Resolve(async ctx =>
            {
                var call = CdpGraphQlCall.From(ctx);
                return await resolve(call, ctx.RequestAborted).ConfigureAwait(false);
            });
    }
}
