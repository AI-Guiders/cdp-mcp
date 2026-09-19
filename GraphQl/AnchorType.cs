#nullable enable
using Cdp.ScriptableIde;
using HotChocolate.Types;

namespace CdpMcp.GraphQl;

/// <summary>Bind Cdp.ScriptableIde.Anchor via ToSpan/ToWire (ADR-0233).</summary>
public sealed class AnchorType : ObjectType<Anchor>
{
    protected override void Configure(IObjectTypeDescriptor<Anchor> descriptor)
    {
        descriptor.Name("Anchor");
        descriptor.Description("Cdp.ScriptableIde.Anchor — locus SSOT (ADR-0233).");

        descriptor.Field("wire")
            .Type<NonNullType<StringType>>()
            .Resolve(ctx => ctx.Parent<Anchor>().ToWire());

        descriptor.Field("file")
            .Type<StringType>()
            .Resolve(ctx => ctx.Parent<Anchor>().ToSpan().File);

        descriptor.Field("member")
            .Type<StringType>()
            .Resolve(ctx => ctx.Parent<Anchor>().ToSpan().MemberKey);

        descriptor.Field("lineStart")
            .Type<IntType>()
            .Resolve(ctx => ctx.Parent<Anchor>().ToSpan().LineStart);

        descriptor.Field("lineEnd")
            .Type<IntType>()
            .Resolve(ctx => ctx.Parent<Anchor>().ToSpan().LineEnd);

        descriptor.Field("role")
            .Type<StringType>()
            .Resolve(ctx => ctx.Parent<Anchor>().ToSpan().Role);
    }
}
