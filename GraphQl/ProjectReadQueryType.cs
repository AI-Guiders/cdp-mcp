#nullable enable
using HotChocolate.Types;

namespace CdpMcp.GraphQl;

/// <summary>Explicit Project nested type — BindFieldsExplicitly.</summary>
internal sealed class ProjectReadQueryType : ObjectType<ProjectReadQuery>
{
    protected override void Configure(IObjectTypeDescriptor<ProjectReadQuery> descriptor)
    {
        descriptor.Name("Project");
        descriptor.BindFieldsExplicitly();

        descriptor.Field(x => x.Scene(default)).Name("scene");
        descriptor.Field(x => x.Recent(default)).Name("recent");
        descriptor
            .Field("list")
            .Argument("root", a => a.Type<StringType>())
            .Type<ObjectType<EngineEnvelopeNode>>()
            .Resolve(async ctx =>
            {
                var parent = ctx.Parent<ProjectReadQuery>();
                return await parent.List(
                    ctx.ArgumentValue<string?>("root"),
                    ctx.RequestAborted).ConfigureAwait(false);
            });
    }
}
