using System.Text.Json;
using Cdp.Core;
using Cdp.Lsp;
using Cdp.ScriptableIde;
using Tomlyn;

namespace CdpMcp;

internal sealed partial class CdpSettings
{
    public string DefaultPhase { get; init; } = "explore";
    public string DefaultObject { get; init; } = "kb";
    public MemorySettings Memory { get; init; } = new();
    public DevSettings Dev { get; init; } = new();
    public LanguageRegistry Languages { get; init; } = LanguageRegistry.Default;
    public IReadOnlyList<LspLaunchPreset> LspPresets { get; init; } = LspLaunchPreset.BuiltInDefaults;
    public IntentWorkspaceSettings IntentWorkspace { get; init; } = new();
    public CockpitHostSettings CockpitHost { get; init; } = new();
    public VendorCatalogOptions Vendor { get; init; } = VendorCatalog.CreateBuiltInDefaults();

    public static readonly string[] DefaultWorldRoots = ["worlds", "META"];
    /// <summary>Project cards + personal Kolb/free-theme parks (interim; no separate memory_personal domain).</summary>
    public static readonly string[] DefaultProjectRoots = ["work/projects", "personal"];
    public static readonly string[] DefaultSkillRoots = ["domains", "templates"];

    public static CdpSettings Load(string? path)
    {
        path ??= Path.Combine(AppContext.BaseDirectory, "config", "cdp-mcp.toml");
        if (!File.Exists(path))
            return new CdpSettings();

        var doc = TomlSerializer.Deserialize<CdpTomlDocument>(
            File.ReadAllText(path),
            new TomlSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })
            ?? new CdpTomlDocument();

        var memory = doc.Memory ?? new CdpTomlMemory();
        var self = memory.Self ?? new CdpTomlMemorySelf();
        var dev = doc.Dev ?? new CdpTomlDev();
        var intentWs = doc.IntentWorkspace ?? new CdpTomlIntentWorkspace();
        var cockpitHost = doc.CockpitHost ?? new CdpTomlCockpitHost();

        return new CdpSettings
        {
            DefaultPhase = string.IsNullOrWhiteSpace(doc.DefaultPhase) ? "recall" : doc.DefaultPhase!,
            DefaultObject = string.IsNullOrWhiteSpace(doc.DefaultObject) ? "kb" : doc.DefaultObject!,
            Memory = new MemorySettings
            {
                NotesConfig = memory.NotesConfig,
                TaskConfig = memory.TaskConfig,
                World = Facet(memory.World, DefaultWorldRoots),
                Project = Facet(memory.Project, DefaultProjectRoots),
                Task = Enabled(memory.Task),
                Session = Enabled(memory.Session),
                Skill = Facet(memory.Skill, DefaultSkillRoots),
                Self = new MemorySelfSettings
                {
                    Finding = Enabled(self.Finding),
                    Failure = Enabled(self.Failure)
                }
            },
            Dev = new DevSettings
            {
                Debug = Enabled(dev.Debug),
                Build = Enabled(dev.Build),
                Roslyn = Enabled(dev.Roslyn),
                Git = Enabled(dev.Git),
                CodebaseIndex = Enabled(dev.CodebaseIndex),
                Anui = Enabled(dev.Anui)
            },
            Languages = BuildLanguageRegistry(doc.Languages),
            LspPresets = BuildLspPresets(doc.Languages?.Lsp),
            IntentWorkspace = new IntentWorkspaceSettings
            {
                DatabasePath = string.IsNullOrWhiteSpace(intentWs.DatabasePath) ? null : intentWs.DatabasePath.Trim()
            },
            CockpitHost = new CockpitHostSettings
            {
                Exe = string.IsNullOrWhiteSpace(cockpitHost.Exe) ? null : cockpitHost.Exe.Trim()
            },
            Vendor = VendorCatalog.CreateBuiltInDefaults()
        };
    }


    private sealed class CdpTomlDocument
    {
        public string? DefaultPhase { get; set; }
        public string? DefaultObject { get; set; }
        public CdpTomlMemory? Memory { get; set; }
        public CdpTomlDev? Dev { get; set; }
        public CdpTomlLanguages? Languages { get; set; }
        public CdpTomlIntentWorkspace? IntentWorkspace { get; set; }
        public CdpTomlCockpitHost? CockpitHost { get; set; }
    }

    private sealed class CdpTomlCockpitHost
    {
        public string? Exe { get; set; }
    }

    private sealed class CdpTomlIntentWorkspace
    {
        public string? DatabasePath { get; set; }
    }

    private sealed class CdpTomlLanguages
    {
        public string[]? Ids { get; set; }
        public Dictionary<string, string>? Aliases { get; set; }
        public CdpTomlLanguageDetect[]? Detect { get; set; }
        public CdpTomlLspPreset[]? Lsp { get; set; }
    }

    private sealed class CdpTomlLspPreset
    {
        public string? Id { get; set; }
        public string? Command { get; set; }
        public string[]? Args { get; set; }
        public string[]? LanguageIds { get; set; }
        public string[]? RootMarkers { get; set; }
    }

    private sealed class CdpTomlLanguageDetect
    {
        public string? Id { get; set; }
        public string? Kind { get; set; }
        public int? Priority { get; set; }
        public string? Extension { get; set; }
        public string? FileName { get; set; }
    }

    private sealed class CdpTomlMemory
    {
        public string? NotesConfig { get; set; }
        public string? TaskConfig { get; set; }
        public CdpTomlFacet? World { get; set; }
        public CdpTomlFacet? Project { get; set; }
        public CdpTomlToggle? Task { get; set; }
        public CdpTomlToggle? Session { get; set; }
        public CdpTomlFacet? Skill { get; set; }
        public CdpTomlMemorySelf? Self { get; set; }
    }

    private sealed class CdpTomlMemorySelf
    {
        public CdpTomlToggle? Finding { get; set; }
        public CdpTomlToggle? Failure { get; set; }
    }

    private sealed class CdpTomlDev
    {
        public CdpTomlToggle? Debug { get; set; }
        public CdpTomlToggle? Build { get; set; }
        public CdpTomlToggle? Roslyn { get; set; }
        public CdpTomlToggle? Git { get; set; }
        public CdpTomlToggle? CodebaseIndex { get; set; }
        public CdpTomlToggle? Anui { get; set; }
    }

    private sealed class CdpTomlFacet
    {
        public bool Enabled { get; set; } = true;
        public string[]? Roots { get; set; }
    }

    private sealed class CdpTomlToggle
    {
        public bool Enabled { get; set; } = true;
    }
}

internal sealed class MemorySettings
{
    public string? NotesConfig { get; init; }
    public string? TaskConfig { get; init; }
    public MemoryFacetSettings World { get; init; } = new() { Roots = CdpSettings.DefaultWorldRoots };
    public MemoryFacetSettings Project { get; init; } = new() { Roots = CdpSettings.DefaultProjectRoots };
    public MemoryToggleSettings Task { get; init; } = new();
    public MemoryToggleSettings Session { get; init; } = new();
    public MemoryFacetSettings Skill { get; init; } = new() { Roots = CdpSettings.DefaultSkillRoots };
    public MemorySelfSettings Self { get; init; } = new();
}

internal sealed class MemorySelfSettings
{
    public MemoryToggleSettings Finding { get; init; } = new();
    public MemoryToggleSettings Failure { get; init; } = new();
}

internal sealed class DevSettings
{
    public MemoryToggleSettings Debug { get; init; } = new();
    public MemoryToggleSettings Build { get; init; } = new();
    public MemoryToggleSettings Roslyn { get; init; } = new();
    public MemoryToggleSettings Git { get; init; } = new();
    public MemoryToggleSettings CodebaseIndex { get; init; } = new();
    public MemoryToggleSettings Anui { get; init; } = new();
}

internal sealed class MemoryFacetSettings
{
    public bool Enabled { get; init; } = true;
    public IReadOnlyList<string> Roots { get; init; } = [];
}

internal sealed class MemoryToggleSettings
{
    public bool Enabled { get; init; } = true;
}

internal sealed class IntentWorkspaceSettings
{
    public string? DatabasePath { get; init; }
}

/// <summary>Process-layer operator GUI host (ADR-0019 companion). Remount to pick up exe=.</summary>
internal sealed class CockpitHostSettings
{
    /// <summary>Absolute path to CascadeIDE / thin shell exe. Override at start with path=; env is escape only.</summary>
    public string? Exe { get; init; }
}
