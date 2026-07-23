using System.Text.Json;
using AgentTaskKnowledge.Core;
using Cdp.Core;

namespace CdpMcp.Backends;

internal sealed class TaskKnowledgeBackend : ICdpBackendModule
{
    private readonly AgentTaskKnowledgeMcp.ToolHandlers? _handlers;
    private readonly bool _enabled;

    public TaskKnowledgeBackend(CdpSettings settings)
    {
        _enabled = settings.Memory.Task.Enabled;
        if (!_enabled)
            return;

        var args = string.IsNullOrWhiteSpace(settings.Memory.TaskConfig)
            ? Array.Empty<string>()
            : ["--config", settings.Memory.TaskConfig];
        var code = TaskKnowledgeBootstrap.TryLoadSettings(args, out var localSettings, out var err);
        if (code != 0)
        {
            // Allow unconfigured legacy store resolution via workspace_path only.
            Console.Error.WriteLine(err ?? "TK config warning");
        }
        else if (localSettings is not null)
        {
            TaskKnowledgeRuntime.Initialize(localSettings, TaskKnowledgeBootstrap.LoadedConfigPath);
        }

        _handlers = new AgentTaskKnowledgeMcp.ToolHandlers(new TaskKnowledgeStore());
    }

    public string Domain => CdpDomains.MemoryTask;
    public bool IsEnabled => _enabled && _handlers is not null;
    public string HealthSummary => IsEnabled ? "up (in-proc TaskKnowledge.Core)" : "disabled";
    public IReadOnlyList<ToolAffordance> Affordances =>
        Wave1AffordanceSeed.Build().Where(a => a.Domain == Domain).ToArray();

    public ValueTask<string> CallAsync(string underlyingName, IReadOnlyDictionary<string, JsonElement> args) =>
        ValueTask.FromResult(_handlers!.Handle(underlyingName, args));
}
