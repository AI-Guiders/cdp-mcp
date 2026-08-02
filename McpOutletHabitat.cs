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

}
