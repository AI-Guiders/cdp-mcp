using AgentNotes.Core;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

internal static class WritingCanonHostPathsResolver
{
    private static readonly HashSet<string> CodeLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "csharp", "typescript", "python", "powershell", "delphi",
    };

    internal static WritingCanonHostPaths FromSession(
        SessionContext session,
        CdpSettings settings,
        DocumentBufferStore? docStore)
    {
        string? primary = AgentNotesRuntime.TryGetPrimaryKnowledgeRoot(out var root) ? root : null;
        var sessionLang = NormalizeCodeLanguage(IdeSessionLifecycle.ResolveLanguage(session));
        return new WritingCanonHostPaths
        {
            PrimaryKnowledgeRoot = primary,
            GuidersStyleRoot = settings.Canon.GuidersStyleRoot,
            SessionLanguage = sessionLang,
            BufferLanguage = InferBufferLanguage(docStore),
        };
    }

    private static string? NormalizeCodeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language) || CdpLanguages.IsAny(language))
            return null;
        var lang = language.Trim();
        return CodeLanguages.Contains(lang) ? lang : null;
    }

    private static string? InferBufferLanguage(DocumentBufferStore? docStore)
    {
        if (docStore is null)
            return null;

        foreach (var buf in docStore.All)
        {
            var fromBuf = NormalizeCodeLanguage(buf.Language);
            if (fromBuf is not null)
                return fromBuf;

            var guessed = DocumentBufferStore.GuessLanguage(buf.Path);
            var fromPath = NormalizeCodeLanguage(guessed);
            if (fromPath is not null)
                return fromPath;
        }

        return null;
    }
}
