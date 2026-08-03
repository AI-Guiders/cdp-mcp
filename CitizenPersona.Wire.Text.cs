namespace CdpMcp;

internal static partial class CitizenPersona
{
    // Property — not static field init: Head/Tail live in other partials; field order across
    // files is undefined and can concatenate null+null → "".
    public static string WireSystemPrompt =>
        (WireSystemPromptHead + WireSystemPromptTail)
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Trim();
}
