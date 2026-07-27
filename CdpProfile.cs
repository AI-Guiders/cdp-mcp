#nullable enable

namespace CdpMcp;

/// <summary>
/// Process profile isolation (ADR 0199). <c>CDP_PROFILE</c> scopes WitDB / pressure / state root.
/// Profile <c>default</c> keeps legacy flat <c>%LocalAppData%/cdp-mcp/</c> paths.
/// </summary>
internal static class CdpProfile
{
    public static string Name { get; } = Normalize(Environment.GetEnvironmentVariable("CDP_PROFILE"));

    public static string StateRoot { get; } = ResolveStateRoot(Name);

    public static bool IsDefault => Name.Equals("default", StringComparison.OrdinalIgnoreCase);

    static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "default";
        var s = raw.Trim().ToLowerInvariant();
        Span<char> buf = stackalloc char[s.Length];
        var n = 0;
        foreach (var c in s)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
                buf[n++] = c;
        }

        if (n == 0)
            return "default";
        return new string(buf[..n]);
    }

    static string ResolveStateRoot(string name)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (name.Equals("default", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(local, "cdp-mcp");
        return Path.Combine(local, "cdp-mcp", "profiles", name);
    }
}
