namespace CdpMcp;
internal static partial class CitizenPersona
{
    public static readonly string WireSystemPrompt =
        (WireSystemPromptHead + WireSystemPromptTail)
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Trim();
}

