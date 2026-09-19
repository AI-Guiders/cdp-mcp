#nullable enable
using HotChocolate.Types;

namespace CdpMcp.GraphQl;

/// <summary>Nested Git read — BindFieldsExplicitly (HC 16).</summary>
internal sealed class GitReadQueryType : ObjectType<GitReadQuery>
{
    protected override void Configure(IObjectTypeDescriptor<GitReadQuery> descriptor)
    {
        descriptor.Name("Git");
        descriptor.BindFieldsExplicitly();

        descriptor.Field(x => x.Scene(default)).Name("scene");
        descriptor.Field(x => x.Status(default)).Name("status");
        descriptor
            .Field("diff")
            .Argument("path", a => a.Type<StringType>())
            .Argument("staged", a => a.Type<BooleanType>().DefaultValue(false))
            .Type<ObjectType<EngineEnvelopeNode>>()
            .Resolve(async ctx =>
            {
                var parent = ctx.Parent<GitReadQuery>();
                return await parent.Diff(
                    ctx.ArgumentValue<string?>("path"),
                    ctx.ArgumentValue<bool>("staged"),
                    ctx.RequestAborted).ConfigureAwait(false);
            });
        descriptor
            .Field("preflight")
            .Argument("staged", a => a.Type<BooleanType>().DefaultValue(false))
            .Type<ObjectType<EngineEnvelopeNode>>()
            .Resolve(async ctx =>
            {
                var parent = ctx.Parent<GitReadQuery>();
                return await parent.Preflight(
                    ctx.ArgumentValue<bool>("staged"),
                    ctx.RequestAborted).ConfigureAwait(false);
            });
    }
}
