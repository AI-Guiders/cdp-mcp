#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CdpMcp;

internal sealed partial class McpOutletHabitat : IAsyncDisposable
{
	private sealed record PresetDef(string Command, string[] Args, string Note);

	private sealed record ToolCard(string Name, string? Description);

	private sealed class MountedServer(string Id, string Kind, string Command, string[] Args, string? Note, McpClient Client, DateTimeOffset MountedUtc, IReadOnlyList<ToolCard> Tools) : IAsyncDisposable
	{
		public string Id { get; } = Id;

		public string Kind { get; } = Kind;

		public McpClient Client { get; } = Client;

		public IReadOnlyList<ToolCard> Tools { get; private set; } = Tools;

		public void ReplaceTools(IReadOnlyList<ToolCard> tools)
		{
			Tools = tools;
		}

		public object Card(bool includeToolsSample = false)
		{
			return new
			{
				id = Id,
				kind = Kind,
				command = Command,
				args = Args,
				note = Note,
				mounted_utc = MountedUtc.ToString("O"),
				tool_count = Tools.Count,
				tools_sample = (includeToolsSample ? (from t in Tools.Take(12)
					select t.Name) : null)
			};
		}

		public async ValueTask DisposeAsync()
		{
			try
			{
				await Client.DisposeAsync().ConfigureAwait(continueOnCapturedContext: false);
			}
			catch
			{
			}
		}
	}

	public const string Schema = "mcp_outlet/v1";

	private static readonly JsonSerializerOptions Pretty = new JsonSerializerOptions
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
	};

	private readonly ConcurrentDictionary<string, MountedServer> _servers = new ConcurrentDictionary<string, MountedServer>(StringComparer.OrdinalIgnoreCase);

	private static readonly Dictionary<string, PresetDef> Presets = new Dictionary<string, PresetDef>(StringComparer.OrdinalIgnoreCase)
	{
		["memory"] = new PresetDef("npx", new string[2] { "-y", "@modelcontextprotocol/server-memory" }, "Official MCP memory server — light dogfood guest."),
		["serena"] = new PresetDef("uvx", new string[6] { "--from", "git+https://github.com/oraios/serena", "serena", "start-mcp-server", "--context", "ide-assistant" }, "Symbol-level IDE brain (LSP). Heavy first start — uvx download."),
		["filesystem"] = new PresetDef("npx", new string[3] { "-y", "@modelcontextprotocol/server-filesystem", "." }, "Filesystem MCP rooted at cwd — override args= for path."),
		["time"] = new PresetDef("npx", new string[2] { "-y", "@modelcontextprotocol/server-time" }, "Tiny time server — fastest smoke mount if published.")
	};

	private static IReadOnlyList<string> PresetIds => Presets.Keys.OrderBy<string, string>((string k) => k, StringComparer.OrdinalIgnoreCase).ToList();

	public static string[] KnownPresetIds => Presets.Keys.OrderBy<string, string>((string k) => k, StringComparer.OrdinalIgnoreCase).ToArray();

	public string SceneJson()
	{
		List<object> list = (from s in _servers.Values.OrderBy<MountedServer, string>((MountedServer s) => s.Id, StringComparer.OrdinalIgnoreCase)
			select s.Card()).ToList();
		return JsonSerializer.Serialize(new
		{
			schema = "mcp_outlet/v1",
			ok = true,
			op = "scene",
			count = list.Count,
			servers = list,
			presets = PresetIds,
			next = new object[4]
			{
				new
				{
					go = "mcp_mount",
					label = "Mount",
					why = "preset=serena|memory|… or command="
				},
				new
				{
					go = "mcp_tools",
					label = "Tools",
					why = "server= id — shortlist child tools"
				},
				new
				{
					go = "mcp_call",
					label = "Call",
					why = "server= + tool= + args="
				},
				new
				{
					go = "mcp_unmount",
					label = "Unmount",
					why = "server= id"
				}
			},
			hint = "Equal-standing MCP panel: you mount guests for a task, then unmount. Child tools never flood host ListTools — use cdp_mcp op=tools|call."
		}, Pretty);
	}

	/// <summary>Cheap desk pulse — no ListTools / no child round-trip.</summary>
	public McpPulse Pulse()
	{
		var n = _servers.Count;
		return new McpPulse(true, n == 0 ? "mcp · idle" : $"mcp · {n} mounted", n);
	}

	public readonly record struct McpPulse(bool Ok, string Line, int Count);

	/// <summary>Mirror outlet pulse to flat CIDE chrome latch (not EICAS).</summary>
	public void PublishGlass()
	{
		var p = Pulse();
		// Dark Cockpit: silent when idle (0 guests).
		CideMcpLatch.Publish(active: p.Count > 0, pulse: p.Line, mounted: p.Count);
	}

	public static McpOutletHabitat? Instance { get; private set; }

	public McpOutletHabitat()
	{
		Instance = this;
	}

	public string PresetsJson()
	{
		return JsonSerializer.Serialize(new
		{
			schema = "mcp_outlet/v1",
			ok = true,
			op = "presets",
			presets = Enumerable.Select(Presets, (KeyValuePair<string, PresetDef> kv) => new
			{
				id = kv.Key,
				command = kv.Value.Command,
				args = kv.Value.Args,
				note = kv.Value.Note
			})
		}, Pretty);
	}

	public async Task<string> MountAsync(IReadOnlyDictionary<string, JsonElement> args, CancellationToken cancellationToken)
	{
		string text = Opt(args, "preset");
		string text2 = Opt(args, "command");
		if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(text2))
		{
			text = IdeSettingsHabitat.EffectiveMcpDefaultPreset();
		}
		string id = Opt(args, "id") ?? Opt(args, "server") ?? text ?? "mcp";
		id = SanitizeId(id);
		if (_servers.ContainsKey(id))
		{
			return Fail("already_mounted", id, "Unmount first or pick another id=");
		}
		string note = null;
		string cmd;
		string[] cmdArgs;
		string kind;
		if (!string.IsNullOrWhiteSpace(text))
		{
			if (!Presets.TryGetValue(text.Trim(), out PresetDef value))
			{
				return Fail("unknown_preset", text, "op=presets for catalog");
			}
			cmd = value.Command;
			cmdArgs = value.Args;
			kind = "preset:" + text.Trim().ToLowerInvariant();
			note = value.Note;
		}
		else
		{
			if (string.IsNullOrWhiteSpace(text2))
			{
				return Fail("need_preset_or_command", null, "preset=serena|memory|… or command= + args=[]");
			}
			cmd = text2.Trim();
			cmdArgs = ReadStringArray(args, "args") ?? Array.Empty<string>();
			kind = "stdio";
		}
		string name = Opt(args, "name") ?? id;
		McpClient client;
		try
		{
			client = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
			{
				Name = name,
				Command = cmd,
				Arguments = cmdArgs
			}), null, null, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			return Fail("mount_failed", id, cmd + ": " + ex.Message);
		}
		IReadOnlyList<ToolCard> tools;
		try
		{
			tools = await ListToolCardsAsync(client, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex2)
		{
			await SafeDisposeAsync(client).ConfigureAwait(continueOnCapturedContext: false);
			return Fail("list_tools_failed", id, ex2.Message);
		}
		MountedServer mountedServer = new MountedServer(id, kind, cmd, cmdArgs, note, client, DateTimeOffset.UtcNow, tools);
		if (!_servers.TryAdd(id, mountedServer))
		{
			await mountedServer.DisposeAsync().ConfigureAwait(continueOnCapturedContext: false);
			return Fail("already_mounted", id, null);
		}
		PublishGlass();
		return JsonSerializer.Serialize(new
		{
			schema = "mcp_outlet/v1",
			ok = true,
			op = "mount",
			server = mountedServer.Card(includeToolsSample: true),
			hint = "op=tools server=" + id + " → op=call server=" + id + " tool=…"
		}, Pretty);
	}

	public async Task<string> ToolsAsync(IReadOnlyDictionary<string, JsonElement> args, CancellationToken cancellationToken)
	{
		if (!TryGet(args, out MountedServer server, out string errorJson))
		{
			return errorJson;
		}
		string filter = Opt(args, "filter") ?? Opt(args, "q");
		int take = OptInt(args, "take") ?? 40;
		take = Math.Clamp(take, 1, 200);
		try
		{
			IReadOnlyList<ToolCard> tools = await ListToolCardsAsync(server.Client, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			server.ReplaceTools(tools);
		}
		catch (Exception ex)
		{
			return Fail("list_tools_failed", server.Id, ex.Message);
		}
		IEnumerable<ToolCard> source = server.Tools;
		if (!string.IsNullOrWhiteSpace(filter))
		{
			source = source.Where((ToolCard t) => t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || (t.Description?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false));
		}
		List<ToolCard> list = source.Take(take).ToList();
		return JsonSerializer.Serialize(new
		{
			schema = "mcp_outlet/v1",
			ok = true,
			op = "tools",
			server = server.Id,
			total = server.Tools.Count,
			shown = list.Count,
			filter = filter,
			tools = list,
			hint = "op=call server=" + server.Id + " tool=<name> args={…}"
		}, Pretty);
	}
}
