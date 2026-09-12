#nullable enable
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Serialization;

namespace Cdp.Config;

public static class CdpConfigTomlMerge
{
    public static string Merge(string baseToml, string overlayToml)
    {
        var target = TomlSerializer.Deserialize<TomlTable>(baseToml)
            ?? throw new InvalidOperationException("Embedded defaults TOML is empty.");
        var overlay = TomlSerializer.Deserialize<TomlTable>(overlayToml)
            ?? new TomlTable();

        MergeInto(target, overlay);
        return TomlSerializer.Serialize(target);
    }

    static void MergeInto(TomlTable target, TomlTable overlay)
    {
        foreach (var (key, value) in overlay)
        {
            if (target.TryGetValue(key, out var existing)
                && existing is TomlTable existingTable
                && value is TomlTable overlayTable)
            {
                MergeInto(existingTable, overlayTable);
                continue;
            }

            target[key] = value;
        }
    }
}
