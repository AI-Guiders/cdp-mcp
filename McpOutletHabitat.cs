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

internal sealed class McpOutletHabitat : IAsyncDisposable
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

	public async Task<string> CallAsync(IReadOnlyDictionary<string, JsonElement> args, CancellationToken cancellationToken)
	{
		if (!TryGet(args, out MountedServer server, out string errorJson))
		{
			return errorJson;
		}
		string tool = Opt(args, "tool") ?? Opt(args, "name");
		if (string.IsNullOrWhiteSpace(tool))
		{
			return Fail("tool_required", server.Id, "tool= from op=tools");
		}
		Dictionary<string, object> dictionary = new Dictionary<string, object>(StringComparer.Ordinal);
		if (args.TryGetValue("args", out var value) && value.ValueKind == JsonValueKind.Object)
		{
			foreach (JsonProperty item in value.EnumerateObject())
			{
				dictionary[item.Name] = JsonElementToObject(item.Value);
			}
		}
		foreach (var (key, el) in args)
		{
			if (!IsReserved(key))
			{
				dictionary[key] = JsonElementToObject(el);
			}
		}
		try
		{
			CallToolResult callToolResult = await server.Client.CallToolAsync(tool, dictionary, null, null, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			List<string> list = (from t in callToolResult.Content.OfType<TextContentBlock>()
				select t.Text).ToList();
			int num = callToolResult.Content.Count - list.Count;
			return JsonSerializer.Serialize(new
			{
				schema = "mcp_outlet/v1",
				ok = (callToolResult.IsError != true),
				op = "call",
				server = server.Id,
				tool = tool,
				is_error = (callToolResult.IsError == true),
				text = ((list.Count == 1) ? list[0] : null),
				texts = ((list.Count > 1) ? list : null),
				content_blocks = callToolResult.Content.Count,
				non_text_blocks = num,
				hint = ((num > 0) ? "Non-text content present (image/resource) — inspect host consumer." : null)
			}, Pretty);
		}
		catch (Exception ex)
		{
			return Fail("call_failed", server.Id, tool + ": " + ex.Message);
		}
	}

	public async Task<string> UnmountAsync(IReadOnlyDictionary<string, JsonElement> args)
	{
		string id = Opt(args, "server") ?? Opt(args, "id");
		if (string.IsNullOrWhiteSpace(id))
		{
			return Fail("server_required", null, "server= id from scene");
		}
		if (!_servers.TryRemove(id, out MountedServer value))
		{
			return Fail("not_mounted", id, "op=scene for ids");
		}
		await value.DisposeAsync().ConfigureAwait(continueOnCapturedContext: false);
		PublishGlass();
		return JsonSerializer.Serialize(new
		{
			schema = "mcp_outlet/v1",
			ok = true,
			op = "unmount",
			server = id,
			remaining = _servers.Count
		}, Pretty);
	}

	public async Task<string> DispatchAsync(IReadOnlyDictionary<string, JsonElement> args, CancellationToken cancellationToken)
	{
		string text = Opt(args, "op") ?? "scene";
		string result;
		switch (text.Trim().ToLowerInvariant())
		{
		case "scene":
		case "status":
		case "list":
			result = SceneJson();
			break;
		case "presets":
		case "catalog":
			result = PresetsJson();
			break;
		case "mount":
		case "connect":
		case "add":
			result = await MountAsync(args, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			break;
		case "tools":
		case "list_tools":
			result = await ToolsAsync(args, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			break;
		case "invoke":
		case "call":
			result = await CallAsync(args, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			break;
		case "close":
		case "remove":
		case "unmount":
		case "disconnect":
			result = await UnmountAsync(args).ConfigureAwait(continueOnCapturedContext: false);
			break;
		default:
			result = Fail("unknown_op", text, "op=scene|presets|mount|tools|call|unmount");
			break;
		}
		return result;
	}

	public async ValueTask DisposeAsync()
	{
		string[] array = _servers.Keys.ToArray();
		foreach (string key in array)
		{
			if (_servers.TryRemove(key, out MountedServer value))
			{
				await value.DisposeAsync().ConfigureAwait(continueOnCapturedContext: false);
			}
		}
	}

	private bool TryGet(IReadOnlyDictionary<string, JsonElement> args, [NotNullWhen(true)] out MountedServer? server, out string? errorJson)
	{
		server = null;
		errorJson = null;
		string text = Opt(args, "server") ?? Opt(args, "id");
		if (string.IsNullOrWhiteSpace(text))
		{
			errorJson = Fail("server_required", null, "server= from op=scene");
			return false;
		}
		if (!_servers.TryGetValue(text, out server))
		{
			errorJson = Fail("not_mounted", text, "op=mount first");
			return false;
		}
		return true;
	}

	private static async Task<IReadOnlyList<ToolCard>> ListToolCardsAsync(McpClient client, CancellationToken ct)
	{
		return (await client.ListToolsAsync((RequestOptions?)null, ct).ConfigureAwait(continueOnCapturedContext: false)).Select((McpClientTool t) => new ToolCard(t.Name, Truncate(t.Description, 240))).OrderBy<ToolCard, string>((ToolCard t) => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
	}

	private static async Task SafeDisposeAsync(McpClient client)
	{
		try
		{
			await client.DisposeAsync().ConfigureAwait(continueOnCapturedContext: false);
		}
		catch
		{
		}
	}

	private static string Fail(string error, string? server, string? hint)
	{
		return JsonSerializer.Serialize(new
		{
			schema = "mcp_outlet/v1",
			ok = false,
			error = error,
			server = server,
			hint = hint
		}, Pretty);
	}

	private static string SanitizeId(string id)
	{
		string text = id.Trim();
		if (text.Length == 0)
		{
			return "mcp";
		}
		Span<char> span = stackalloc char[Math.Min(text.Length, 64)];
		int num = 0;
		string text2 = text;
		foreach (char c in text2)
		{
			if (num >= span.Length)
			{
				break;
			}
			int index = num++;
			bool flag = char.IsLetterOrDigit(c);
			if (!flag)
			{
				bool flag2 = ((c == '-' || c == '_') ? true : false);
				flag = flag2;
			}
			span[index] = (flag ? c : '_');
		}
		return new string(span.Slice(0, num));
	}

	private static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
	{
		if (!args.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return null;
		}
		return value.GetString();
	}

	private static int? OptInt(IReadOnlyDictionary<string, JsonElement> args, string key)
	{
		if (!args.TryGetValue(key, out var value))
		{
			return null;
		}
		if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var value2))
		{
			return value2;
		}
		if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var result))
		{
			return result;
		}
		return null;
	}

	private static string[]? ReadStringArray(IReadOnlyDictionary<string, JsonElement> args, string key)
	{
		if (!args.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.Array)
		{
			return null;
		}
		return (from e in value.EnumerateArray()
			where e.ValueKind == JsonValueKind.String
			select e.GetString() into s
			where s.Length > 0
			select s).ToArray();
	}

	private static bool IsReserved(string key)
	{
		switch (key)
		{
		case "id":
		case "op":
		case "filter":
		case "preset":
		case "server":
		case "tool":
		case "name":
		case "args":
		case "take":
		case "link":
		case "command":
		case "q":
		case "uri":
			return true;
		default:
			return false;
		}
	}

	private static object? JsonElementToObject(JsonElement el)
	{
		long value;
		return el.ValueKind switch
		{
			JsonValueKind.String => el.GetString(), 
			JsonValueKind.Number => el.TryGetInt64(out value) ? ((double)value) : el.GetDouble(), 
			JsonValueKind.True => true, 
			JsonValueKind.False => false, 
			JsonValueKind.Null => null, 
			JsonValueKind.Array => el.EnumerateArray().Select(JsonElementToObject).ToList(), 
			JsonValueKind.Object => el.EnumerateObject().ToDictionary<JsonProperty, string, object>((JsonProperty p) => p.Name, (JsonProperty p) => JsonElementToObject(p.Value), StringComparer.Ordinal), 
			_ => el.GetRawText(), 
		};
	}

	private static string? Truncate(string? s, int max)
	{
		if (!string.IsNullOrEmpty(s))
		{
			if (s.Length > max)
			{
				return s.Substring(0, max) + "…";
			}
			return s;
		}
		return s;
	}
}
