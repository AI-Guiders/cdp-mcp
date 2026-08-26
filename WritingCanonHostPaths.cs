using AgentNotes.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

internal static class WritingCanonHostPathsResolver
{
    internal static WritingCanonHostPaths FromCdpSettings(CdpSettings settings)
    {
        string? primary = AgentNotesRuntime.TryGetPrimaryKnowledgeRoot(out var root) ? root : null;
        var styleRoot = settings.Canon.GuidersStyleRoot;
        return new WritingCanonHostPaths
        {
            PrimaryKnowledgeRoot = primary,
            GuidersStyleRoot = styleRoot,
        };
    }
}
