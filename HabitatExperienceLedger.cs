#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Lived habitat experience for any principal (guest tip · citizen · human).
/// Position ladder Junior→Architect. Not SoftFL ontology.
/// Latch: %LocalAppData%/cdp-mcp/habitat-experience-LATEST.json
/// </summary>
internal static class HabitatExperienceLedger
{
    public const string Schema = "habitat_experience/v0";

    public const string DefaultPrincipal = "guest";

    public const int MiddleLessonThreshold = 3;
    public const int SeniorLessonThreshold = 12;
    public const int SeniorBurnThreshold = 3;

    static readonly object Gate = new();
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

    static LatchDoc? Memory;
    static bool DiskHydrated;

    public enum Position
    {
        Junior = 0,
        Middle = 1,
        Senior = 2,
        Architect = 3
    }

    public sealed record Lesson(
        string Id,
        string Principal,
        string Kind,
        string Line,
        string Source,
        string? Organ = null,
        DateTimeOffset? AtUtc = null);

    public sealed record PrincipalState(
        string Principal,
        Position Position,
        int LessonCount,
        bool PositionPinned = false,
        DateTimeOffset? UpdatedUtc = null);

    public sealed record Affordance(
        bool DigWide,
        bool MutateNarrow,
        bool CanPromoteExperience,
        bool CanSeedCurriculum);

    /// <summary>Test hook: redirect latch root.</summary>
    internal static string? RootOverrideForTests { get; set; }

    public static string StateRoot =>
        RootOverrideForTests
        ?? CideIntercomVoiceLatch.RootOverrideForTests
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp");

    public static string LatchPath => Path.Combine(StateRoot, "habitat-experience-LATEST.json");

    public static void ResetForTests()
    {
        lock (Gate)
        {
            Memory = null;
            DiskHydrated = true;
            if (RootOverrideForTests is null)
                return;
            try
            {
                if (File.Exists(LatchPath))
                    File.Delete(LatchPath);
            }
            catch
            {
                /* ignore */
            }
        }
    }

    public static Lesson Record(
        string principal,
        string kind,
        string line,
        string source = "dogfood",
        string? organ = null,
        string? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(line);

        var who = NormalizePrincipal(principal);
        lock (Gate)
        {
            EnsureHydrated();
            Memory ??= EmptyDoc();
            var lesson = new Lesson(
                Id: string.IsNullOrWhiteSpace(id) ? NewId() : id.Trim(),
                Principal: who,
                Kind: kind.Trim().ToLowerInvariant(),
                Line: line.Trim(),
                Source: string.IsNullOrWhiteSpace(source) ? "dogfood" : source.Trim(),
                Organ: string.IsNullOrWhiteSpace(organ) ? null : organ.Trim(),
                AtUtc: DateTimeOffset.UtcNow);

            var lessons = Memory.Lessons.ToList();
            lessons.Add(lesson);
            var principals = Memory.Principals.ToDictionary(
                p => p.Principal,
                p => p,
                StringComparer.OrdinalIgnoreCase);
            principals[who] = RecomputeState(who, lessons, principals.GetValueOrDefault(who));
            Memory = Memory with
            {
                AtUtc = DateTimeOffset.UtcNow,
                Lessons = lessons,
                Principals = principals.Values.OrderBy(p => p.Principal, StringComparer.OrdinalIgnoreCase).ToArray()
            };
            PersistLocked();
            return lesson;
        }
    }

    public static IReadOnlyList<Lesson> Snapshot(string? principal = null)
    {
        lock (Gate)
        {
            EnsureHydrated();
            var all = Memory?.Lessons ?? [];
            if (string.IsNullOrWhiteSpace(principal))
                return all.OrderByDescending(l => l.AtUtc).ToArray();
            var who = NormalizePrincipal(principal);
            return all
                .Where(l => l.Principal.Equals(who, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(l => l.AtUtc)
                .ToArray();
        }
    }

    public static PrincipalState GetPosition(string? principal = null)
    {
        lock (Gate)
        {
            EnsureHydrated();
            var who = NormalizePrincipal(principal);
            var existing = Memory?.Principals
                .FirstOrDefault(p => p.Principal.Equals(who, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
                return existing;
            return RecomputeState(who, Memory?.Lessons ?? [], null);
        }
    }

    public static PrincipalState SetPosition(string principal, Position position, bool pin = true)
    {
        var who = NormalizePrincipal(principal);
        lock (Gate)
        {
            EnsureHydrated();
            Memory ??= EmptyDoc();
            var principals = Memory.Principals.ToDictionary(
                p => p.Principal,
                p => p,
                StringComparer.OrdinalIgnoreCase);
            var count = Memory.Lessons.Count(l => l.Principal.Equals(who, StringComparison.OrdinalIgnoreCase));
            var state = new PrincipalState(who, position, count, PositionPinned: pin, UpdatedUtc: DateTimeOffset.UtcNow);
            principals[who] = state;
            Memory = Memory with
            {
                AtUtc = DateTimeOffset.UtcNow,
                Principals = principals.Values.OrderBy(p => p.Principal, StringComparer.OrdinalIgnoreCase).ToArray()
            };
            PersistLocked();
            return state;
        }
    }

    public static Affordance AffordanceFor(Position position) => position switch
    {
        Position.Junior => new(DigWide: true, MutateNarrow: true, CanPromoteExperience: false, CanSeedCurriculum: false),
        Position.Middle => new(DigWide: true, MutateNarrow: true, CanPromoteExperience: true, CanSeedCurriculum: false),
        Position.Senior => new(DigWide: true, MutateNarrow: false, CanPromoteExperience: true, CanSeedCurriculum: false),
        Position.Architect => new(DigWide: true, MutateNarrow: false, CanPromoteExperience: true, CanSeedCurriculum: true),
        _ => new(DigWide: true, MutateNarrow: true, CanPromoteExperience: false, CanSeedCurriculum: false)
    };

    public static bool TryParsePosition(string? raw, out Position position)
    {
        position = Position.Junior;
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        return Enum.TryParse(raw.Trim(), ignoreCase: true, out position);
    }

    static PrincipalState RecomputeState(
        string who,
        IReadOnlyList<Lesson> lessons,
        PrincipalState? prior)
    {
        var mine = lessons
            .Where(l => l.Principal.Equals(who, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var count = mine.Length;
        if (prior is { PositionPinned: true })
            return prior with { LessonCount = count, UpdatedUtc = DateTimeOffset.UtcNow };

        // dogfood/vision = lived volume; burn/refuse/scar = hard lessons (Senior shortcut).
        var burns = mine.Count(l => l.Kind is "burn" or "refuse" or "scar");
        var computed =
            count >= SeniorLessonThreshold || burns >= SeniorBurnThreshold ? Position.Senior
            : count >= MiddleLessonThreshold ? Position.Middle
            : Position.Junior;

        return new PrincipalState(who, computed, count, PositionPinned: false, UpdatedUtc: DateTimeOffset.UtcNow);
    }

    static string NormalizePrincipal(string? principal)
    {
        if (string.IsNullOrWhiteSpace(principal))
            return DefaultPrincipal;
        return principal.Trim().ToLowerInvariant() switch
        {
            "tip" or "cursor" or "composer" or "harness" => "guest",
            "face" or "sierra" or "fm" => "citizen",
            "op" or "operator" or "human" or "sveta" => "human",
            var p => p
        };
    }

    static string NewId() =>
        "xp-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6];

    static LatchDoc EmptyDoc() => new(Schema, DateTimeOffset.UtcNow, [], []);

    static void EnsureHydrated()
    {
        if (DiskHydrated)
            return;
        DiskHydrated = true;
        Memory = EmptyDoc();
        try
        {
            if (!File.Exists(LatchPath))
                return;
            var json = File.ReadAllText(LatchPath);
            var doc = JsonSerializer.Deserialize<LatchDoc>(json, ReadOpts);
            if (doc is null)
                return;
            Memory = doc with
            {
                Lessons = doc.Lessons ?? [],
                Principals = doc.Principals ?? []
            };
        }
        catch
        {
            /* best-effort */
        }
    }

    static void PersistLocked()
    {
        try
        {
            Directory.CreateDirectory(StateRoot);
            Memory ??= EmptyDoc();
            var tmp = LatchPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Memory, JsonOpts));
            File.Move(tmp, LatchPath, overwrite: true);
        }
        catch
        {
            /* best-effort latch */
        }
    }

    sealed record LatchDoc(
        string Schema,
        DateTimeOffset AtUtc,
        IReadOnlyList<Lesson> Lessons,
        IReadOnlyList<PrincipalState> Principals);
}
