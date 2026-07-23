using System.Text.Json;
using AgentNotes.Core;
using Cdp.Core;

namespace CdpMcp.Backends;

/// <summary>Shared Agent Notes runtime — one init for all memory_* AN facets.</summary>
internal sealed class SharedNotesRuntime
{
    public ToolHandlers Handlers { get; }

    private SharedNotesRuntime(ToolHandlers handlers) => Handlers = handlers;

    public static SharedNotesRuntime? TryCreate(CdpSettings settings)
    {
        var mem = settings.Memory;
        var any = mem.World.Enabled || mem.Project.Enabled || mem.Skill.Enabled || mem.Session.Enabled;
        if (!any)
            return null;

        var args = string.IsNullOrWhiteSpace(mem.NotesConfig)
            ? Array.Empty<string>()
            : ["--config", mem.NotesConfig];
        var code = AgentNotesBootstrap.TryLoadSettings(args, out var localSettings, out var err);
        if (code != 0 || localSettings is null)
            throw new InvalidOperationException(err ?? "Agent Notes config failed.");
        AgentNotesRuntime.Initialize(localSettings, AgentNotesBootstrap.LoadedConfigPath);
        return new SharedNotesRuntime(new ToolHandlers(new NotesStorage()));
    }
}

internal abstract class NotesFacetBackend : ICdpBackendModule
{
    private readonly ToolHandlers _handlers;
    private readonly MemoryScopeGateway? _scope;
    private readonly bool _enabled;

    protected NotesFacetBackend(
        SharedNotesRuntime runtime,
        string domain,
        bool enabled,
        MemoryScopeGateway? scope)
    {
        _handlers = runtime.Handlers;
        Domain = domain;
        _enabled = enabled;
        _scope = scope;
    }

    public string Domain { get; }
    public bool IsEnabled => _enabled;
    public string HealthSummary => IsEnabled
        ? $"up (in-proc AgentNotes.Core / {Domain})"
        : "disabled";
    public IReadOnlyList<ToolAffordance> Affordances =>
        Wave1AffordanceSeed.Build().Where(a => a.Domain == Domain).ToArray();

    public ValueTask<string> CallAsync(string underlyingName, IReadOnlyDictionary<string, JsonElement> args)
    {
        var scoped = _scope is null ? args : _scope.Apply(underlyingName, args);
        return ValueTask.FromResult(_handlers.Handle(underlyingName, scoped));
    }
}

internal sealed class MemoryWorldBackend : NotesFacetBackend
{
    public MemoryWorldBackend(SharedNotesRuntime runtime, CdpSettings settings)
        : base(
            runtime,
            CdpDomains.MemoryWorld,
            settings.Memory.World.Enabled,
            new MemoryScopeGateway(CdpDomains.MemoryWorld, settings.Memory.World.Roots))
    {
    }
}

internal sealed class MemoryProjectBackend : NotesFacetBackend
{
    public MemoryProjectBackend(SharedNotesRuntime runtime, CdpSettings settings)
        : base(
            runtime,
            CdpDomains.MemoryProject,
            settings.Memory.Project.Enabled,
            new MemoryScopeGateway(CdpDomains.MemoryProject, settings.Memory.Project.Roots))
    {
    }
}

internal sealed class MemorySkillBackend : NotesFacetBackend
{
    public MemorySkillBackend(SharedNotesRuntime runtime, CdpSettings settings)
        : base(
            runtime,
            CdpDomains.MemorySkill,
            settings.Memory.Skill.Enabled,
            new MemoryScopeGateway(
                CdpDomains.MemorySkill,
                settings.Memory.Skill.Roots,
                allowPlaybooksUnderWorlds: true))
    {
    }
}

/// <summary>Hot/session AN tools only — knowledge_* rejected even if called directly.</summary>
internal sealed class MemorySessionBackend : ICdpBackendModule
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "route_context",
        "read_hot_context",
        "memory_health",
        "search_agent_notes",
        "upsert_agent_notes_section",
        "validate_sections",
        "normalize_sections"
    };

    private readonly ToolHandlers _handlers;
    private readonly bool _enabled;

    public MemorySessionBackend(SharedNotesRuntime runtime, CdpSettings settings)
    {
        _handlers = runtime.Handlers;
        _enabled = settings.Memory.Session.Enabled;
    }

    public string Domain => CdpDomains.MemorySession;
    public bool IsEnabled => _enabled;
    public string HealthSummary => IsEnabled
        ? "up (in-proc AgentNotes.Core / memory_session)"
        : "disabled";
    public IReadOnlyList<ToolAffordance> Affordances =>
        Wave1AffordanceSeed.Build().Where(a => a.Domain == Domain).ToArray();

    public ValueTask<string> CallAsync(string underlyingName, IReadOnlyDictionary<string, JsonElement> args)
    {
        if (underlyingName.Contains("knowledge", StringComparison.Ordinal)
            || !Allowed.Contains(underlyingName))
        {
            throw new ArgumentException(
                $"memory_session does not expose '{underlyingName}'; use memory_world/project/skill for knowledge/*, or session hot tools only.");
        }

        // Hot notes only — reject knowledge file_path (use memory_* facets).
        if (args.TryGetValue("file_path", out var fp)
            && fp.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(fp.GetString()))
        {
            throw new ArgumentException(
                "memory_session validate/normalize is for hot notes (workspace_path); " +
                "for knowledge file_path use memory_world/project/skill.");
        }

        return ValueTask.FromResult(_handlers.Handle(underlyingName, args));
    }
}
