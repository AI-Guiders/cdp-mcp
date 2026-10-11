#nullable enable

using System.Globalization;

namespace CdpMcp;

/// <summary>
/// Bridge→session bind registry (wake caller identity). A tool call arrives through the
/// caller's CdpMcpBridge (X-CDP-Bridge-Session); once that line claims identity with its
/// opencode session id, auto-arms can target the CALLING window directly instead of the
/// roster single-live-line heuristic (which honestly refuses when 2+ lines are live).
/// Rows: {stamp:O}\tbridge=&lt;id&gt;\tsession=&lt;ses|-&gt;\tnick=&lt;nick|-&gt; — last row per bridge wins.
/// </summary>
internal static class CideIntercomBridgeBind
{
    static readonly object Gate = new();

    public static string StateRoot => CideIntercomVoiceLatch.StateRoot;

    static string BindPath => Path.Combine(StateRoot, "intercom-bridgebind.witdb");

    public static string? RootOverrideForTests { get; set; }

    static string ResolvedRoot => RootOverrideForTests ?? StateRoot;

    static string ResolvedPath => Path.Combine(ResolvedRoot, "intercom-bridgebind.witdb");

    public static void Bind(string? bridge, string? session, string? nick)
    {
        if (string.IsNullOrWhiteSpace(bridge))
            return;
        if (string.IsNullOrWhiteSpace(session) && string.IsNullOrWhiteSpace(nick))
            return;

        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(ResolvedRoot);
                var row = string.Join('\t',
                    DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    "bridge=" + bridge.Trim(),
                    "session=" + (string.IsNullOrWhiteSpace(session) ? "-" : session.Trim()),
                    "nick=" + (string.IsNullOrWhiteSpace(nick) ? "-" : nick.Trim()));
                File.AppendAllText(ResolvedPath, row + Environment.NewLine);
            }
            catch
            {
                /* best-effort registry — wake falls back to the roster heuristic */
            }
        }
    }

    public static (string? Session, string? Nick) Resolve(string? bridge)
    {
        if (string.IsNullOrWhiteSpace(bridge))
            return (null, null);

        lock (Gate)
        {
            try
            {
                if (!File.Exists(ResolvedPath))
                    return (null, null);
                string? session = null;
                string? nick = null;
                foreach (var line in File.ReadLines(ResolvedPath))
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;
                    var parts = line.Split('\t');
                    if (parts.Length < 4)
                        continue;
                    if (!parts[1].StartsWith("bridge=", StringComparison.Ordinal)
                        || !parts[1][7..].Equals(bridge.Trim(), StringComparison.OrdinalIgnoreCase))
                        continue;
                    session = parts[2][8..] is "-" or "" ? null : parts[2][8..];
                    nick = parts[3][5..] is "-" or "" ? null : parts[3][5..];
                }

                return (session, nick);
            }
            catch
            {
                return (null, null);
            }
        }
    }

    public static string? SessionOf(string? bridge) => Resolve(bridge).Session;
}
