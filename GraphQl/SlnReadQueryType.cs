#nullable enable
using HotChocolate.Types;

namespace CdpMcp.GraphQl;

/// <summary>Explicit Sln nested type — BindFieldsExplicitly.</summary>
internal sealed class SlnReadQueryType : ObjectType<SlnReadQuery>
{
    protected override void Configure(IObjectTypeDescriptor<SlnReadQuery> descriptor)
    {
        descriptor.Name("Sln");
        descriptor.BindFieldsExplicitly();

        descriptor
            .Field("list")
            .Argument("root", a => a.Type<StringType>())
            .Type<ObjectType<EngineEnvelopeNode>>()
            .Resolve(async ctx =>
            {
                var parent = ctx.Parent<SlnReadQuery>();
                return await parent.List(
                    ctx.ArgumentValue<string?>("root"),
                    ctx.RequestAborted).ConfigureAwait(false);
            });
        descriptor
            .Field("projects")
            .Argument("solution", a => a.Type<StringType>())
            .Type<ObjectType<EngineEnvelopeNode>>()
            .Resolve(async ctx =>
            {
                var parent = ctx.Parent<SlnReadQuery>();
                return await parent.Projects(
                    ctx.ArgumentValue<string?>("solution"),
                    ctx.RequestAborted).ConfigureAwait(false);
            });
    }
}
