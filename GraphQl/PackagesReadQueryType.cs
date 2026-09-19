#nullable enable
using HotChocolate.Types;

namespace CdpMcp.GraphQl;

/// <summary>Explicit Packages nested type — BindFieldsExplicitly (HC discovery on instance methods).</summary>
internal sealed class PackagesReadQueryType : ObjectType<PackagesReadQuery>
{
    protected override void Configure(IObjectTypeDescriptor<PackagesReadQuery> descriptor)
    {
        descriptor.Name("Packages");
        descriptor.BindFieldsExplicitly();

        descriptor.Field(x => x.List(default)).Name("list");
        descriptor
            .Field("find")
            .Argument("query", a => a.Type<NonNullType<StringType>>())
            .Argument("take", a => a.Type<IntType>().DefaultValue(5))
            .Type<ObjectType<EngineEnvelopeNode>>()
            .Resolve(async ctx =>
            {
                var parent = ctx.Parent<PackagesReadQuery>();
                return await parent.Find(
                    ctx.ArgumentValue<string>("query"),
                    ctx.ArgumentValue<int>("take"),
                    ctx.RequestAborted).ConfigureAwait(false);
            });
        descriptor.Field(x => x.Outdated(default)).Name("outdated");
        descriptor
            .Field("audit")
            .Argument("includeTransitive", a => a.Type<BooleanType>().DefaultValue(true))
            .Type<ObjectType<EngineEnvelopeNode>>()
            .Resolve(async ctx =>
            {
                var parent = ctx.Parent<PackagesReadQuery>();
                return await parent.Audit(
                    ctx.ArgumentValue<bool>("includeTransitive"),
                    ctx.RequestAborted).ConfigureAwait(false);
            });
        descriptor
            .Field("latest")
            .Argument("id", a => a.Type<NonNullType<StringType>>())
            .Argument("includePrerelease", a => a.Type<BooleanType>().DefaultValue(false))
            .Type<ObjectType<EngineEnvelopeNode>>()
            .Resolve(async ctx =>
            {
                var parent = ctx.Parent<PackagesReadQuery>();
                return await parent.Latest(
                    ctx.ArgumentValue<string>("id"),
                    ctx.ArgumentValue<bool>("includePrerelease"),
                    ctx.RequestAborted).ConfigureAwait(false);
            });
        descriptor
            .Field("supplyChain")
            .Argument("root", a => a.Type<StringType>())
            .Type<ObjectType<EngineEnvelopeNode>>()
            .Resolve(async ctx =>
            {
                var parent = ctx.Parent<PackagesReadQuery>();
                return await parent.SupplyChain(
                    ctx.ArgumentValue<string?>("root"),
                    ctx.RequestAborted).ConfigureAwait(false);
            });
        descriptor
            .Field("upgradePlan")
            .Argument("includeTransitive", a => a.Type<BooleanType>().DefaultValue(true))
            .Argument("includePrerelease", a => a.Type<BooleanType>().DefaultValue(false))
            .Type<ObjectType<EngineEnvelopeNode>>()
            .Resolve(async ctx =>
            {
                var parent = ctx.Parent<PackagesReadQuery>();
                return await parent.UpgradePlan(
                    ctx.ArgumentValue<bool>("includeTransitive"),
                    ctx.ArgumentValue<bool>("includePrerelease"),
                    ctx.RequestAborted).ConfigureAwait(false);
            });
    }
}
