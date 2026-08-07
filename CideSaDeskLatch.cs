#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// SoftOrgan sa_desk (cdp_sa) pulse → CIDE quiet chrome (instant).
/// Writes %LocalAppData%/cdp-mcp/sa-desk-LATEST.json; CIDE projector paints
/// WorkspaceChromeBand — not EICAS go=sa. Clean leave (0w/0f) stays silent.
/// </summary>
internal static class CideSaDeskLatch
{
    public const string Schema = "cide_sa_desk_latch/v1";
    public const string OriginAgent = "agent";

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    static readonly JsonSerializerOptions ReadOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>Test hook: redirect latch root.</summary>
    internal static string? RootOverrideForTests { get; set; }

    public static string StateRoot =>
        RootOverrideForTests
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp");

    public static string LatchPath => Path.Combine(StateRoot, "sa-desk-LATEST.json");

    public static void Publish(bool active, string pulse, string? verdict = null)
    {
        // SoftFL lived: Publish (even Task.Run + FileShare) correlated with MCP timeout on go=sa_desk.
        // Dig: SoftBoard stub OK · PulseOnly without Publish OK · with Publish hung. Glass holds
        // sa-desk-LATEST.json. Tests still write under RootOverrideForTests.
        var pulseLine = string.IsNullOrWhiteSpace(pulse) ? "sa_desk · idle" : pulse.Trim();
        var verdictTrim = string.IsNullOrWhiteSpace(verdict) ? null : verdict.Trim();
        var doc = new SaDeskLatchDoc
        {
            Schema = Schema,
            Origin = OriginAgent,
            StampedUtc = DateTimeOffset.UtcNow,
            Active = active,
            Pulse = pulseLine,
            Verdict = verdictTrim,
            ChromeHint = active ? pulseLine : null
        };
        var json = JsonSerializer.Serialize(doc, JsonOpts);
        if (RootOverrideForTests is null)
            return; // production: skip Glass-contended latch write

        try
        {
            Directory.CreateDirectory(StateRoot);
            TryWriteLatchAtomic(json);
        }
        catch { /* best-effort */ }
    }

    static void TryWriteLatchAtomic(string json)
    {
        // Prefer shared overwrite — Glass readers with FileShare.Read must not stall agent MCP.
        try
        {
            using var fs = new FileStream(
                LatchPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete);
            using var writer = new StreamWriter(fs);
            writer.Write(json);
            return;
        }
        catch
        {
            /* fall through to bounded tmp replace */
        }

        var tmp = LatchPath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        File.WriteAllText(tmp, json);
        const int maxAttempts = 8;
        for (var i = 0; i < maxAttempts; i++)
        {
            try
            {
                File.Copy(tmp, LatchPath, overwrite: true);
                try { File.Delete(tmp); } catch { /* orphan ok */ }
                return;
            }
            catch (IOException) when (i < maxAttempts - 1)
            {
                Thread.Sleep(15 + i * 10);
            }
            catch (UnauthorizedAccessException) when (i < maxAttempts - 1)
            {
                Thread.Sleep(15 + i * 10);
            }
        }

        try { File.Delete(tmp); } catch { /* orphan cleaned on next ship */ }
    }

    public static SaDeskLatchDoc? TryRead()
    {
        try
        {
            if (!File.Exists(LatchPath))
                return null;
            var raw = File.ReadAllText(LatchPath);
            var doc = JsonSerializer.Deserialize<SaDeskLatchDoc>(raw, ReadOpts);
            if (doc is null || !string.Equals(doc.Schema, Schema, StringComparison.OrdinalIgnoreCase))
                return null;
            return doc;
        }
        catch
        {
            return null;
        }
    }

    public sealed class SaDeskLatchDoc
    {
        public string Schema { get; set; } = CideSaDeskLatch.Schema;
        public string Origin { get; set; } = OriginAgent;
        public DateTimeOffset StampedUtc { get; set; }
        public bool Active { get; set; }
        public string? Pulse { get; set; }
        public string? Verdict { get; set; }
        public string? ChromeHint { get; set; }
    }
}
