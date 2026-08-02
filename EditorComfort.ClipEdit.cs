using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

internal static partial class EditorComfort
{
    static string Copy(DocumentBufferStore store, SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var (text, from) = ExtractSpan(store, session, args);
        var frame = SessionClipboard.Push(text, from, "copy");

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "copy",
            frame = frame.Id,
            chars = text.Length,
            from,
            clipboard = SessionClipboard.Summary(),
            next = new object[]
            {
                new { go = "paste", label = "Paste frame", why = $"frame={frame.Id} place=after|before|sniper" },
                new { go = "clipboard", label = "Clipboard", why = "Android-style frame list" },
                new { go = "cut", label = "Cut instead", why = "same span → frame + remove" }
            },
            hint = $"Pushed frame {frame.Id} (MRU). Paste keeps it unless preserve=false."
        }, Pretty);
    }


}
