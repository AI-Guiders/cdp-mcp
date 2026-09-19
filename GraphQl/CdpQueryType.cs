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
    }
}
