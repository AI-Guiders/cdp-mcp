using Cdp.ScriptableIde;

namespace CdpMcp.IntentWorkspace;

/// <summary>Open Recent backed by intent-workspace WitDB.</summary>
internal sealed class WitDbOpenRecentBackend(IntentWorkspaceStore store, string databasePath) : IOpenRecentBackend
{
    public string Location => databasePath;

    public void Push(string path, string? root, string? kind, string? language) =>
        store.OpenRecentPush(path, root, kind, language);

    public IReadOnlyList<OpenRecentStore.Entry> List(int take)
    {
        return store.OpenRecentList(take)
            .Select(e => new OpenRecentStore.Entry(e.Path, e.Root, e.Kind, e.Language, e.OpenedUtc))
            .ToArray();
    }

    public OpenRecentStore.Entry? TryGet(int zeroBasedIndex)
    {
        var all = List(OpenRecentStore.DefaultCapacity);
        if (zeroBasedIndex < 0 || zeroBasedIndex >= all.Count)
            return null;
        return all[zeroBasedIndex];
    }
}
