#nullable enable
using System.Globalization;
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Recall fallback when tenant stash is empty — ignite-wake-LATEST, peer stash, canonical course.
/// ChargePolicy promises ignite-wake-LATEST.course when recall empty; tool must honor it.
/// </summary>
internal static partial class IdePressureChannel
{
    sealed class PressureRecallResolved
    {
        public string? Body { get; init; }
        public string? Source { get; init; }
        public string? SourcePath { get; init; }
        public string? WakeTask { get; init; }
    }

    static PressureRecallResolved ResolveRecallBody(PressureDoc? doc)
    {
        if (doc?.Body is { Length: > 0 } body)
        {
            return new PressureRecallResolved
            {
                Body = body,
                Source = "tenant_stash",
                SourcePath = FilePath
            };
        }

        var peer = TryPeekPeerTenantStash();
        if (peer is { Body.Length: > 0 })
            return peer;

        var wake = IdeIgniteWakeLatch.TryRead();
        if (wake?.Course is { Length: > 0 } course)
        {
            return new PressureRecallResolved
            {
                Body = course,
                Source = "ignite_wake_latch",
                SourcePath = IdeIgniteWakeLatch.LatchPath,
                WakeTask = wake.Task
            };
        }

        var canonical = TryPeekSealedCourse();
        if (canonical is { Length: > 0 })
        {
            return new PressureRecallResolved
            {
                Body = canonical,
                Source = "canonical_sealed_course"
            };
        }

        return new PressureRecallResolved();
    }

    static PressureRecallResolved? TryPeekPeerTenantStash()
    {
        try
        {
            var tenantsRoot = ResolveWorkspaceTenantsRoot();
            if (tenantsRoot is null)
                return null;

            var current = Path.GetFullPath(FilePath);
            PressureRecallResolved? best = null;
            var bestUtc = DateTime.MinValue;

            foreach (var stashFile in Directory.EnumerateFiles(tenantsRoot, "pressure-stash.json", SearchOption.AllDirectories))
            {
                if (string.Equals(Path.GetFullPath(stashFile), current, StringComparison.OrdinalIgnoreCase))
                    continue;

                PressureDoc? peer;
                try
                {
                    peer = JsonSerializer.Deserialize<PressureDoc>(File.ReadAllText(stashFile), JsonOpts);
                }
                catch
                {
                    continue;
                }

                if (peer?.Body is not { Length: > 0 })
                    continue;

                var utc = ParseStashUtc(peer.StashUtc);
                if (utc <= bestUtc)
                    continue;

                bestUtc = utc;
                best = new PressureRecallResolved
                {
                    Body = peer.Body,
                    Source = "peer_tenant_stash",
                    SourcePath = stashFile
                };
            }

            return best;
        }
        catch
        {
            return null;
        }
    }

    static DateTime ParseStashUtc(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return DateTime.MinValue;
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
            ? dt
            : DateTime.MinValue;
    }

    static bool SsotSufficientForRecall(string? body, PressureDoc? doc) =>
        body is { Length: >= 40 }
        && (HasOperatorPriority(body)
            || doc?.PlanNote is { Length: > 0 }
            || doc?.IgniteNote is { Length: > 0 });

    /// <summary>.../ws/{hash}/tenants — peer recall only within same workspace instance.</summary>
    static string? ResolveWorkspaceTenantsRoot()
    {
        var dir = Path.GetFullPath(CdpProfile.StateRoot);
        while (!string.IsNullOrEmpty(dir))
        {
            if (string.Equals(Path.GetFileName(dir), "tenants", StringComparison.OrdinalIgnoreCase))
                return dir;
            dir = Directory.GetParent(dir)?.FullName ?? "";
        }

        return null;
    }

    /// <summary>Wake tier SSOT — tenant, peer, or ignite-wake course (not canonical-only).</summary>
    internal static bool HasRecallSsotForWake()
    {
        var doc = Load();
        if (doc?.Body is { Length: > 0 })
            return true;
        if (TryPeekPeerTenantStash()?.Body is { Length: > 0 })
            return true;
        return IdeIgniteWakeLatch.TryRead()?.Course is { Length: > 0 };
    }
}
