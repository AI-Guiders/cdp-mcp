#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>FlattenJson, last-run, path/wire helpers for ScriptScene.</summary>
internal static partial class ScriptScene
{
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
