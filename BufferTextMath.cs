namespace CdpMcp;

/// <summary>
/// Canonical buffer-text math shared by buffer meta (line_count) and comfort ops.
/// Physical lines: a trailing newline does NOT add a virtual line ("a\nb\n" = 2 lines).
/// Peek parity: File.ReadLines on "a\nb\n" also yields 2 (trailing fragment not emitted).
/// OffsetOf still accepts the virtual EOF line (line_count+1, col 1) for positions;
/// end positions clamp instead of throwing — OffsetOfEnd in DocumentBufferStore.
/// </summary>
internal static class BufferTextMath
{
    public static int CountLines(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 1;
        var n = 0;
        foreach (var ch in text)
        {
            if (ch == '\n')
                n++;
        }

        if (text[^1] != '\n')
            n++;
        return Math.Max(1, n);
    }
}
