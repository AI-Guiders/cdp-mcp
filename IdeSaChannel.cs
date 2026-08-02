#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=sa_desk</c> / Meta <c>cdp_sa</c> — agent-native pre-refactor SA (ADR-0010).
/// Axes: locus / scope / depth. Not EICAS <c>go=sa</c>.
/// </summary>
internal static partial class IdeSaChannel
{
    public const string SchemaVersion = "code_sa/v1";
    public const string ToolName = "cdp_sa";

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static string HandleJson(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args) =>
        JsonSerializer.Serialize(Handle(store, session, args), Pretty);

    public static object Handle(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        var depth = NormDepth(Opt(args, "depth") ?? Opt(args, "shape") ?? "slim");
        var locus = ResolveLocus(store, session, args);
        var scope = NormScope(Opt(args, "scope"), locus);

        if (depth == "pulse")
            return PulseOnly(store, session, locus, scope);

        var gates = RunGates(store, session, locus, scope);
        var dirty = IdeReviewChannel.ListDirtyFiles(session.ProjectRoot);
        var dirtyHit = FindDirtyForLocus(dirty, locus.Path, session.ProjectRoot);
        ClonesSnap? clones = depth == "full" ? TryClones(store, session, locus, scope, depth) : null;
        var (verdict, why) = Decide(gates, dirtyHit, clones);
        var pulse = FormatPulse(verdict, gates, dirtyHit, clones);
        PublishGlass(pulse, verdict, gates);
        return BuildDeskPayload(
            depth, locus, scope, pulse, verdict, why, gates,
            TakeFindings(gates, depth == "full" ? 12 : 5),
            dirtyHit, dirty.Count, clones, BuildNext(locus, scope, verdict));
    }

    static string FormatPulse(
        string verdict,
        GatesSnap gates,
        IdeReviewChannel.FileCard? dirtyHit,
        ClonesSnap? clones) =>
        $"sa_desk · {verdict} · {gates.Warn}w/{gates.Fail}f" +
        (dirtyHit is not null ? $" · dirty:{dirtyHit.Risk}" : "") +
        (clones is { Groups: > 0 } ? $" · clones:{clones.Groups}" : "");

    static object BuildDeskPayload(
        string depth,
        Locus locus,
        string scope,
        string pulse,
        string verdict,
        string why,
        GatesSnap gates,
        object topFindings,
        IdeReviewChannel.FileCard? dirtyHit,
        int dirtyCount,
        ClonesSnap? clones,
        object next)
    {
        if (depth == "full")
        {
            return new
            {
                ok = true,
                schema = SchemaVersion,
                role = "sa_desk",
                go = "sa_desk",
                tool = ToolName,
                detail = "full",
                locus = LocusCard(locus),
                scope,
                depth,
                pulse,
                verdict,
                why,
                quality = new { gates.Ok, gates.Enabled, gates.Warn, gates.Fail, gates.Pulse, findings = topFindings },
                dirty = dirtyHit is null ? null : FileCardDto(dirtyHit),
                dirty_count = dirtyCount,
                clones,
                next,
                hint = "Pre-refactor SA. Verdict is heuristic — confirm blast via find_usages at locus."
            };
        }

        return new
        {
            ok = true,
            schema = SchemaVersion,
            role = "sa_desk",
            go = "sa_desk",
            tool = ToolName,
            detail = "slim",
            locus = LocusCard(locus),
            scope,
            depth,
            pulse,
            verdict,
            why,
            quality = new { gates.Warn, gates.Fail, findings = topFindings },
            dirty = dirtyHit is null ? null : FileCardDto(dirtyHit),
            clones = clones is null ? null : new { clones.Ok, clones.Groups, clones.Pulse },
            next,
            hint = "depth=full for clones detail + more findings; go=sa is EICAS (different)."
        };
    }

    static object PulseOnly(
        DocumentBufferStore store,
        SessionContext session,
        Locus locus,
        string scope)
    {
        var gates = RunGates(store, session, locus, scope);
        var dirty = IdeReviewChannel.ListDirtyFiles(session.ProjectRoot);
        var dirtyHit = FindDirtyForLocus(dirty, locus.Path, session.ProjectRoot);
        var (verdict, why) = Decide(gates, dirtyHit, clones: null);
        var pulse = $"sa_desk · {verdict} · {gates.Warn}w/{gates.Fail}f";
        PublishGlass(pulse, verdict, gates);
        return new
        {
            ok = true,
            schema = SchemaVersion,
            role = "sa_desk",
            go = "sa_desk",
            tool = ToolName,
            detail = "pulse",
            pulse,
            verdict,
            why,
            locus = LocusCard(locus),
            scope,
            next = BuildNext(locus, scope, verdict),
            hint = "depth=slim for findings."
        };
    }

    /// <summary>Quiet chrome for CIDE — not EICAS. Clean leave clears the band.</summary>
    static void PublishGlass(string pulse, string verdict, GatesSnap gates)
    {
        try
        {
            var leaveClean = string.Equals(verdict, "leave", StringComparison.OrdinalIgnoreCase)
                && gates.Warn == 0
                && gates.Fail == 0;
            CideSaDeskLatch.Publish(active: !leaveClean, pulse, verdict);
        }
        catch
        {
            /* best-effort */
        }
    }

    static GatesSnap RunGates(
        DocumentBufferStore store,
        SessionContext session,
        Locus locus,
        string scope)
    {
        object raw;
        if ((scope is "file" or "buffer") && locus.Path is { Length: > 0 })
        {
            EnsureOpen(store, locus.Path);
            raw = QualityGates.EvaluatePath(store, session.ProjectRoot, locus.Path);
        }
        else
        {
            raw = QualityGates.EvaluateStore(store, session.ProjectRoot);
        }

        return ParseGates(raw);
    }

    static void EnsureOpen(DocumentBufferStore store, string path)
    {
        try
        {
            if (File.Exists(path))
                store.Open(path);
        }
        catch
        {
            // gates may still report buffer_not_open
        }
    }

    static GatesSnap ParseGates(object raw)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(raw));
        var root = doc.RootElement;
        var ok = !root.TryGetProperty("ok", out var okEl) || okEl.ValueKind != JsonValueKind.False;
        var enabled = !root.TryGetProperty("enabled", out var enEl) || enEl.ValueKind != JsonValueKind.False;
        var warn = root.TryGetProperty("warn", out var wEl) && wEl.TryGetInt32(out var wn) ? wn : 0;
        var fail = root.TryGetProperty("fail", out var fEl) && fEl.TryGetInt32(out var fn) ? fn : 0;
        var pulse = root.TryGetProperty("pulse", out var pEl) ? pEl.GetString() ?? "" : "";
        var findings = new List<Finding>();
        if (root.TryGetProperty("findings", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
            {
                findings.Add(new Finding(
                    item.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                    item.TryGetProperty("severity", out var sev) ? sev.GetString() ?? "" : "",
                    item.TryGetProperty("path", out var path) ? path.GetString() : null,
                    item.TryGetProperty("symbol", out var sym) ? sym.GetString() : null,
                    item.TryGetProperty("message", out var msg) ? msg.GetString() ?? "" : "",
                    item.TryGetProperty("go", out var go) ? go.GetString() : null));
            }
        }

        return new GatesSnap(ok, enabled, warn, fail, pulse, findings);
    }

    static ClonesSnap? TryClones(
        DocumentBufferStore store,
        SessionContext session,
        Locus locus,
        string scope,
        string depth)
    {
        try
        {
            var cloneScope = (scope is "project" or "dirty") && depth == "full" ? "project" : "file";
            var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["scope"] = JsonSerializer.SerializeToElement(cloneScope),
                ["max_groups"] = JsonSerializer.SerializeToElement(depth == "full" ? 20 : 5),
                ["max_files"] = JsonSerializer.SerializeToElement(depth == "full" ? 200 : 40)
            };
            if (locus.Path is { Length: > 0 })
                dict["path"] = JsonSerializer.SerializeToElement(locus.Path);

            var json = CodeClones.Run(store, session, dict);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
            var pulse = root.TryGetProperty("pulse", out var pEl) ? pEl.GetString() ?? "" : "";
            var groups = 0;
            if (root.TryGetProperty("clone_groups", out var gEl) && gEl.ValueKind == JsonValueKind.Array)
                groups = gEl.GetArrayLength();
            else if (root.TryGetProperty("groups", out var g2) && g2.ValueKind == JsonValueKind.Array)
                groups = g2.GetArrayLength();

            object? sample = null;
            if (depth == "full" && root.TryGetProperty("clone_groups", out var fullGroups))
                sample = JsonSerializer.Deserialize<object>(fullGroups.GetRawText());

            return new ClonesSnap(ok, groups, pulse, sample);
        }
        catch (Exception ex)
        {
            return new ClonesSnap(false, 0, $"clones error: {ex.Message}", null);
        }
    }
}
