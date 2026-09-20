#nullable enable
using HotChocolate.Types;

namespace CdpMcp.GraphQl;

/// <summary>Nested knowledge read — BindFieldsExplicitly (HC 16).</summary>
internal sealed class KnowledgeReadQueryType : ObjectType<KnowledgeReadQuery>
{
    protected override void Configure(IObjectTypeDescriptor<KnowledgeReadQuery> descriptor)
    {
        descriptor.Name("Knowledge");
        descriptor.BindFieldsExplicitly();

        descriptor
            .Field("recall")
            .Argument("query", a => a.Type<NonNullType<StringType>>())
            .Argument("layer", a => a.Type<StringType>())
            .Argument("first", a => a.Type<IntType>().DefaultValue(15))
            .Type<ObjectType<KnowledgeRecallResult>>()
            .Resolve(async ctx =>
            {
                var parent = ctx.Parent<KnowledgeReadQuery>();
                return await parent.Recall(
                    ctx.ArgumentValue<string>("query"),
                    ctx.ArgumentValue<string?>("layer"),
                    ctx.ArgumentValue<int>("first"),
                    ctx.RequestAborted).ConfigureAwait(false);
            });

        descriptor
            .Field("tags")
            .Argument("query", a => a.Type<StringType>())
            .Argument("mode", a => a.Type<StringType>().DefaultValue("inventory"))
            .Type<ObjectType<EngineEnvelopeNode>>()
            .Resolve(async ctx =>
            {
                var parent = ctx.Parent<KnowledgeReadQuery>();
                return await parent.Tags(
                    ctx.ArgumentValue<string?>("query"),
                    ctx.ArgumentValue<string>("mode"),
                    ctx.RequestAborted).ConfigureAwait(false);
            });

        descriptor
            .Field("read")
            .Argument("path", a => a.Type<NonNullType<StringType>>())
            .Argument("mode", a => a.Type<StringType>().DefaultValue("full"))
            .Type<ObjectType<EngineEnvelopeNode>>()
            .Resolve(async ctx =>
            {
                var parent = ctx.Parent<KnowledgeReadQuery>();
                return await parent.Read(
                    ctx.ArgumentValue<string>("path"),
                    ctx.ArgumentValue<string>("mode"),
                    ctx.RequestAborted).ConfigureAwait(false);
            });

        descriptor
            .Field("list")
            .Argument("subdir", a => a.Type<StringType>())
            .Type<ObjectType<EngineEnvelopeNode>>()
            .Resolve(async ctx =>
            {
                var parent = ctx.Parent<KnowledgeReadQuery>();
                return await parent.List(
                    ctx.ArgumentValue<string?>("subdir"),
                    ctx.RequestAborted).ConfigureAwait(false);
            });

        descriptor
            .Field("definition")
            .Argument("definitionId", a => a.Type<NonNullType<StringType>>())
            .Argument("packId", a => a.Type<StringType>())
            .Argument("packPath", a => a.Type<StringType>())
            .Type<ObjectType<EngineEnvelopeNode>>()
            .Resolve(async ctx =>
            {
                var parent = ctx.Parent<KnowledgeReadQuery>();
                return await parent.Definition(
                    ctx.ArgumentValue<string>("definitionId"),
                    ctx.ArgumentValue<string?>("packId"),
                    ctx.ArgumentValue<string?>("packPath"),
                    ctx.RequestAborted).ConfigureAwait(false);
            });

        descriptor
            .Field("procedure")
            .Argument("procedureId", a => a.Type<StringType>())
            .Argument("packId", a => a.Type<StringType>())
            .Argument("packPath", a => a.Type<StringType>())
            .Type<ObjectType<EngineEnvelopeNode>>()
            .Resolve(async ctx =>
            {
                var parent = ctx.Parent<KnowledgeReadQuery>();
                return await parent.Procedure(
                    ctx.ArgumentValue<string?>("procedureId"),
                    ctx.ArgumentValue<string?>("packId"),
                    ctx.ArgumentValue<string?>("packPath"),
                    ctx.RequestAborted).ConfigureAwait(false);
            });

        descriptor
            .Field("process")
            .Argument("processId", a => a.Type<StringType>())
            .Argument("packId", a => a.Type<StringType>())
            .Argument("packPath", a => a.Type<StringType>())
            .Type<ObjectType<EngineEnvelopeNode>>()
            .Resolve(async ctx =>
            {
                var parent = ctx.Parent<KnowledgeReadQuery>();
                return await parent.Process(
                    ctx.ArgumentValue<string?>("processId"),
                    ctx.ArgumentValue<string?>("packId"),
                    ctx.ArgumentValue<string?>("packPath"),
                    ctx.RequestAborted).ConfigureAwait(false);
            });

        descriptor
            .Field("packs")
            .Argument("packId", a => a.Type<StringType>())
            .Argument("packPath", a => a.Type<StringType>())
            .Type<ObjectType<EngineEnvelopeNode>>()
            .Resolve(async ctx =>
            {
                var parent = ctx.Parent<KnowledgeReadQuery>();
                return await parent.Packs(
                    ctx.ArgumentValue<string?>("packId"),
                    ctx.ArgumentValue<string?>("packPath"),
                    ctx.RequestAborted).ConfigureAwait(false);
            });

        descriptor
            .Field("radiusGate")
            .Argument("deltaRadius", a => a.Type<NonNullType<FloatType>>())
            .Argument("openHypothesisCount", a => a.Type<IntType>())
            .Argument("claim", a => a.Type<StringType>())
            .Type<ObjectType<EngineEnvelopeNode>>()
            .Resolve(async ctx =>
            {
                var parent = ctx.Parent<KnowledgeReadQuery>();
                return await parent.RadiusGate(
                    ctx.ArgumentValue<double>("deltaRadius"),
                    ctx.ArgumentValue<int?>("openHypothesisCount"),
                    ctx.ArgumentValue<string?>("claim"),
                    ctx.RequestAborted).ConfigureAwait(false);
            });
    }
}
