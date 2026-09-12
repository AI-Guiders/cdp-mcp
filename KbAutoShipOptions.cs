using AgentNotes.Core;

namespace CdpMcp;

/// <summary>CDP-ADR-0224: KB AutoShip FileWatcher — configured roots + debounce.</summary>
internal sealed class KbAutoShipOptions
{
    public const int DefaultDebounceMs = 90_000;
    public const string DefaultBranch = "main";

    public bool Enabled { get; init; } = true;
    public int DebounceMs { get; init; } = DefaultDebounceMs;
    public IReadOnlyList<string> Roots { get; init; } = [];
    public string Branch { get; init; } = DefaultBranch;

    public static KbAutoShipOptions FromSettings(CdpSettings settings)
    {
        var toml = settings.KbAutoShip;
        var roots = ResolveRoots(settings, toml);
        return new KbAutoShipOptions
        {
            Enabled = toml.Enabled ?? true,
            DebounceMs = toml.DebounceMs is > 0 ? toml.DebounceMs.Value : DefaultDebounceMs,
            Roots = roots,
            Branch = string.IsNullOrWhiteSpace(toml.Branch) ? DefaultBranch : toml.Branch.Trim()
        };
    }

    static IReadOnlyList<string> ResolveRoots(CdpSettings settings, KbAutoShipTomlSettings toml)
    {
        if (toml.Roots is { Length: > 0 })
        {
            return toml.Roots
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r => Path.GetFullPath(r.Trim()))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        if (string.IsNullOrWhiteSpace(settings.Memory.NotesConfig))
            return [];

        try
        {
            var args = new[] { "--config", settings.Memory.NotesConfig };
            if (AgentNotesBootstrap.TryLoadSettings(args, out var localSettings, out _) != 0
                || localSettings?.PrimaryKnowledgeRoot is not { Length: > 0 } primary)
            {
                return [];
            }

            var gitRoot = GitSessionDefaults.TryResolveScmRoot(primary) ?? primary;
            return [Path.GetFullPath(gitRoot)];
        }
        catch
        {
            return [];
        }
    }
}

internal sealed class KbAutoShipTomlSettings
{
    public bool? Enabled { get; init; }
    public int? DebounceMs { get; init; }
    public string[]? Roots { get; init; }
    public string? Branch { get; init; }
}
