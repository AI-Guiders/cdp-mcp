#nullable enable
using System;
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

/// <summary>Call/Unmount/Dispatch + Dispose; helpers in Ops.Helpers.</summary>
internal sealed partial class McpOutletHabitat
{
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

}
