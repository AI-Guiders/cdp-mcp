#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>Path resolve, remember/board, FlattenJson helpers for ScriptScene.</summary>
internal static partial class ScriptScene
{
	private static async Task<object?> TryBufferDiagnosticsAsync(DocumentBufferStore store, SessionContext session, IReadOnlyDictionary<string, ICdpBackendModule> byDomain, string full, CancellationToken ct)
	{
		try
		{
			Dictionary<string, JsonElement> args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				["op"] = JsonSerializer.SerializeToElement("diagnostics"),
				["path"] = JsonSerializer.SerializeToElement(full),
				["force"] = JsonSerializer.SerializeToElement(value: true)
			};
			return JsonSerializer.Deserialize<JsonElement>(await DocumentEditPlane.DispatchAsync("cdp_buffer", store, session, byDomain, args, ct).ConfigureAwait(continueOnCapturedContext: false));
		}
		catch
		{
			return null;
		}
	}

	private static void FlushIfOpen(DocumentBufferStore store, string full)
	{
		DocBuffer docBuffer = store.All.FirstOrDefault((DocBuffer b) => string.Equals(b.Path, full, StringComparison.OrdinalIgnoreCase));
		if (docBuffer != null && docBuffer.Dirty)
		{
			store.Flush(docBuffer, allowShrink: true);
		}
	}

	private static bool TryResolveScriptPath(DocumentBufferStore store, SessionContext session, IReadOnlyDictionary<string, JsonElement> args, out string full, out string error)
	{
		full = "";
		error = "path_required";
		string text = OptString(args, "path") ?? OptString(args, "file");
		string text2 = OptString(args, "name");
		if (text != null && text.Length > 0)
		{
			full = (Path.IsPathRooted(text) ? Path.GetFullPath(text) : Path.GetFullPath(Path.Combine(session.ProjectRoot ?? ScriptsRoot(session) ?? ".", text)));
			if (!File.Exists(full))
			{
				string text3 = ScriptsRoot(session);
				if (text3 != null)
				{
					string text4 = Path.Combine(text3, Path.GetFileName(text));
					if (File.Exists(text4))
					{
						full = text4;
					}
				}
			}
		}
		else
		{
			if (text2 != null && text2.Length > 0)
			{
				string text5 = ScriptsRoot(session);
				if (text5 != null)
				{
					string text6 = Path.GetFileName(text2);
					if (!text6.EndsWith(".csx", StringComparison.OrdinalIgnoreCase))
					{
						text6 += ".csx";
					}
					full = Path.Combine(text5, text6);
					goto IL_0134;
				}
			}
			DocBuffer docBuffer = store.All.FirstOrDefault((DocBuffer b) => b.Path.EndsWith(".csx", StringComparison.OrdinalIgnoreCase));
			if (docBuffer != null)
			{
				full = docBuffer.Path;
			}
		}
		goto IL_0134;
		IL_0134:
		if (full.Length == 0 || !File.Exists(full))
		{
			error = ((full.Length == 0) ? "path_required" : "not_found");
			return false;
		}
		return true;
	}

	private static string? ScriptsRoot(SessionContext session)
	{
		string projectRoot = session.ProjectRoot;
		if (projectRoot == null || projectRoot.Length <= 0)
		{
			return null;
		}
		return Path.Combine(projectRoot, ".cdp", "scripts");
	}

	private static object[] ListScripts(string? root)
	{
		if (root == null || !Directory.Exists(root))
		{
			return Array.Empty<object>();
		}
		return Directory.EnumerateFiles(root, "*.csx").OrderByDescending(File.GetLastWriteTimeUtc).Take(40)
			.Select((Func<string, object>)((string p) => new
			{
				name = Path.GetFileName(p),
				path = p,
				mtime_utc = File.GetLastWriteTimeUtc(p)
			}))
			.ToArray();
	}

	private static void Remember(SessionContext session, string path, string mode, bool ok, string? bodyJson = null, string? pulse = null, string[]? board = null)
	{
		string text = CapText(bodyJson, 12000);
		string[] board2 = ((board != null && board.Length > 0) ? board.Take(24).ToArray() : BuildBoardLines(path, mode, ok, text));
		string pulse2 = ((pulse != null && pulse.Length > 0) ? TrimPulse(pulse) : DefaultPulse(path, mode, ok));
		LastByRoot[SessionKey(session)] = new LastRun(path, mode, ok, DateTime.UtcNow, pulse2, text, board2);
		try
		{
			Store?.ScriptLastRunSave(SessionKey(session), path, mode, ok, DateTime.UtcNow, pulse2, text, board2);
		}
		catch
		{
		}
	}

	private static string? CapText(string? text, int max)
	{
		if (text == null)
		{
			return null;
		}
		if (text.Length <= max)
		{
			return text;
		}
		return text.Substring(0, max) + "…";
	}

	private static string TrimPulse(string s)
	{
		if (s.Length > 96)
		{
			return s.Substring(0, 95) + "…";
		}
		return s;
	}

	private static string DefaultPulse(string path, string mode, bool ok)
	{
		string fileName = Path.GetFileName(path);
		if (!ok)
		{
			return mode + " FAIL · " + fileName;
		}
		return mode + " ok · " + fileName;
	}

	private static string[] BuildBoardLines(string path, string mode, bool ok, string? body)
	{
		List<string> list = new List<string> { (ok ? "*" : "!") + Path.GetFileName(path) + " · " + mode };
		if (body != null && body.Length > 0)
		{
			try
			{
				using JsonDocument jsonDocument = JsonDocument.Parse(body);
				FlattenJson(jsonDocument.RootElement, list, 0);
			}
			catch
			{
				foreach (string item in body.Replace("\r\n", "\n").Split('\n').Take(23))
				{
					if (!string.IsNullOrWhiteSpace(item))
					{
						list.Add("|--- " + TrimPulse(item.Trim()));
					}
				}
			}
		}
		return list.Take(24).ToArray();
	}

	private static void FlattenJson(JsonElement el, List<string> lines, int depth)
	{
		if (lines.Count >= 24)
		{
			return;
		}
		string text = new string(' ', Math.Min(depth, 4) * 2);
		switch (el.ValueKind)
		{
		case JsonValueKind.Object:
		{
			foreach (JsonProperty item in el.EnumerateObject())
			{
				if (lines.Count >= 24)
				{
					break;
				}
				JsonValueKind valueKind = item.Value.ValueKind;
				if (valueKind - 1 <= JsonValueKind.Object)
				{
					lines.Add(text + "|--- " + item.Name);
					FlattenJson(item.Value, lines, depth + 1);
					continue;
				}
				string text2 = item.Value.ToString();
				if (text2.Length > 80)
				{
					text2 = text2.Substring(0, 79) + "…";
				}
				lines.Add($"{text}|--- {item.Name}: {text2}");
			}
			break;
		}
		case JsonValueKind.Array:
		{
			int value = 0;
			{
				foreach (JsonElement item2 in el.EnumerateArray())
				{
					if (lines.Count >= 24 || value++ >= 8)
					{
						break;
					}
					JsonValueKind valueKind = item2.ValueKind;
					if (valueKind - 1 <= JsonValueKind.Object)
					{
						lines.Add($"{text}|--- [{value}]");
						FlattenJson(item2, lines, depth + 1);
					}
					else
					{
						lines.Add($"{text}|--- {item2}");
					}
				}
				break;
			}
		}
		default:
			lines.Add($"{text}|--- {el}");
			break;
		}
	}

	public static LastRun? TryGetLast(SessionContext session)
	{
		string text = SessionKey(session);
		if (LastByRoot.TryGetValue(text, out LastRun value))
		{
			return value;
		}
		try
		{
			(string, string, bool, DateTime, string, string, string[])? tuple = Store?.ScriptLastRunTryLoad(text);
			if (tuple.HasValue)
			{
				(string, string, bool, DateTime, string, string, string[]) valueOrDefault = tuple.GetValueOrDefault();
				value = new LastRun(valueOrDefault.Item1, valueOrDefault.Item2, valueOrDefault.Item3, valueOrDefault.Item4, valueOrDefault.Item5, valueOrDefault.Item6, valueOrDefault.Item7);
				LastByRoot[text] = value;
				return value;
			}
		}
		catch
		{
		}
		return null;
	}

	private static object? TryParseBody(string? bodyJson)
	{
		if (bodyJson == null || bodyJson.Length <= 0)
		{
			return null;
		}
		try
		{
			return JsonSerializer.Deserialize<JsonElement>(bodyJson);
		}
		catch
		{
			return bodyJson;
		}
	}

	private static string SessionKey(SessionContext session)
	{
		return session.ProjectRoot ?? session.ScmRoot ?? "_";
	}

	private static string Rel(SessionContext session, string abs)
	{
		string text = session.ProjectRoot ?? session.ScmRoot;
		if (text == null)
		{
			return abs.Replace('\\', '/');
		}
		try
		{
			string text2 = Path.GetFullPath(text).TrimEnd(new char[2]
			{
				Path.DirectorySeparatorChar,
				Path.AltDirectorySeparatorChar
			});
			string fullPath = Path.GetFullPath(abs);
			if (fullPath.StartsWith(text2, StringComparison.OrdinalIgnoreCase))
			{
				return fullPath.Substring(text2.Length).TrimStart(new char[2] { '\\', '/' }).Replace('\\', '/');
			}
		}
		catch
		{
		}
		return abs.Replace('\\', '/');
	}

	private static string RelWire(SessionContext session, string abs)
	{
		return "[F:" + Rel(session, abs) + "]";
	}

	private static int LineCount(string text)
	{
		return text.Replace("\r\n", "\n").Split('\n').Length;
	}

	private static object[] Next(params (string go, string label, string why)[] items)
	{
		return ((IEnumerable<(string, string, string)>)items).Select((Func<(string, string, string), object>)(((string go, string label, string why) i) => new { i.go, i.label, i.why })).ToArray();
	}

	private static string Err(string error, string? op, string? hint)
	{
		return JsonSerializer.Serialize(new
		{
			schema = "script_scene/v0",
			ok = false,
			op = op,
			error = error,
			hint = hint
		}, Pretty);
	}

	private static string? OptString(IReadOnlyDictionary<string, JsonElement> args, string key)
	{
		if (!args.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return null;
		}
		return value.GetString();
	}

	private static bool BoolOr(IReadOnlyDictionary<string, JsonElement> args, string key, bool fallback)
	{
		if (!args.TryGetValue(key, out var value))
		{
			return fallback;
		}
		if (value.ValueKind == JsonValueKind.True)
		{
			return true;
		}
		if (value.ValueKind == JsonValueKind.False)
		{
			return false;
		}
		if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var result))
		{
			return result;
		}
		return fallback;
	}
}
