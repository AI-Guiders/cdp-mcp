using System.Text;

namespace CdpMcp;

/// <summary>
/// Disk write that never truncates the destination mid-flight (temp + replace).
/// Parallel writers on the same path still need <see cref="PathMutateGate"/>.
/// </summary>
internal static class AtomicTextFile
{
    static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void WriteUtf8(string path, string text)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full);
        if (dir is { Length: > 0 })
            Directory.CreateDirectory(dir);

        var tmp = full + "." + Guid.NewGuid().ToString("N")[..8] + ".cdp-tmp";
        try
        {
            File.WriteAllText(tmp, text ?? "", Utf8NoBom);
            File.Move(tmp, full, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(tmp))
                    File.Delete(tmp);
            }
            catch
            {
                // best-effort cleanup
            }

            throw;
        }
    }
}
