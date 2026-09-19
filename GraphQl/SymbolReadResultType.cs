#nullable enable
using Cdp.ScriptableIde;
using HotChocolate.Types;

namespace CdpMcp.GraphQl;

/// <summary>Symbol read result — BindFieldsExplicitly (Anchor on Declaration).</summary>
internal sealed class SymbolReadResultType : ObjectType<SymbolReadResult>
{
    protected override void Configure(IObjectTypeDescriptor<SymbolReadResult> descriptor)
    {
        descriptor.Name("SymbolReadResult");
        descriptor.BindFieldsExplicitly();
        descriptor.Field(x => x.Name);
        descriptor.Field(x => x.File);
        descriptor.Field(x => x.Declaration).Type<AnchorType>();
        descriptor.Field(x => x.Usages);
        descriptor.Field(x => x.Hint);
    }
}
