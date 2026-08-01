#nullable enable
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Scope latch disk IO (≤ADX soft-warn peel).</summary>
internal static partial class IdeScopeChannel
{
    static ScopeDoc? Load()
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(FilePath))
                    return null;
                return JsonSerializer.Deserialize<ScopeDoc>(File.ReadAllText(FilePath), JsonOpts);
            }
            catch
            {
                return null;
            }
        }
    }

    static void Save(ScopeDoc doc)
    {
        lock (Gate)
        {
            var dir = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(dir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(doc, JsonOpts), Encoding.UTF8);
            File.Move(tmp, FilePath, overwrite: true);
        }

        PublishGlass();
    }

    sealed class ScopeDoc
    {
        public string Schema { get; set; } = SchemaVersion;
        public string? Primary { get; set; }
        public string? Scope { get; set; }
        public string? SetUtc { get; set; }
        public string? ProjectRoot { get; set; }
        public string? Source { get; set; }
    }
}
