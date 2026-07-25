#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cdp.Core;
using Cdp.ScriptableIde;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

internal static class ScriptScene
{
	public sealed record LastRun(string Path, string Mode, bool Ok, DateTime AtUtc, string Pulse, string? BodyJson, string[] Board);

	public const string Schema = "script_scene/v0";

	public const string ToolName = "cdp_script_scene";

	private static readonly JsonSerializerOptions Pretty = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	private static readonly ConcurrentDictionary<string, LastRun> LastByRoot = new ConcurrentDictionary<string, LastRun>(StringComparer.OrdinalIgnoreCase);

	private static IntentWorkspaceStore? Store;

	private const int BodyCapChars = 12000;

	private const int BoardCapLines = 24;

	public static void Bind(IntentWorkspaceStore? store)
	{
		Store = store;
	}

	/// <summary>Cheap seat pulse — no full scene JSON.</summary>
	public static (bool Ok, string Pulse) Pulse(SessionContext session)
	{
		if (session.ProjectRoot is not { Length: > 0 })
			return (true, "no project — cdp_open");
		var n = ListScripts(ScriptsRoot(session)).Length;
		return (true, n == 0 ? "scripts ready — put" : $"{n} script(s)");
	}

	public static bool IsScriptTool(string name)
	{
		return string.Equals(name, "cdp_script_scene", StringComparison.OrdinalIgnoreCase);
	}

	public static async Task<string> DispatchAsync(DocumentBufferStore store, SessionContext session, IReadOnlyDictionary<string, ICdpBackendModule> byDomain, IReadOnlyDictionary<string, JsonElement> args, Func<string, IReadOnlyDictionary<string, JsonElement>, CancellationToken, Task<string>> metaDispatch, CancellationToken ct = default(CancellationToken))
	{
		string text = (OptString(args, "op") ?? OptString(args, "feature") ?? "scene").Trim().ToLowerInvariant();
		string result;
		switch (text)
		{
		case "scene":
		case "map":
		case "status":
		case "":
			result = SceneMap(session);
			break;
		case "put":
		case "new":
		case "create":
			result = await PutAsync(store, session, byDomain, args, ct).ConfigureAwait(continueOnCapturedContext: false);
			break;
		case "open":
			result = await OpenAsync(store, session, byDomain, args, ct).ConfigureAwait(continueOnCapturedContext: false);
			break;
		case "check":
		case "compile":
			result = await CheckAsync(store, session, byDomain, args, ct).ConfigureAwait(continueOnCapturedContext: false);
			break;
		case "run":
		case "dryrun":
		case "dry_run":
			result = await RunAsync(store, session, args, metaDispatch, ct).ConfigureAwait(continueOnCapturedContext: false);
			break;
		case "report":
		case "last":
			result = Last(session);
			break;
		case "help":
			result = Help(args);
			break;
		default:
			result = Err("unknown_op", text, "op=scene|put|open|check|run|last|help");
			break;
		}
		return result;
	}

	private static string SceneMap(SessionContext session)
	{
		string text = ScriptsRoot(session);
		string projectRoot = session.ProjectRoot;
		bool flag = projectRoot != null && projectRoot.Length > 0;
		object[] array = ListScripts(text);
		LastByRoot.TryGetValue(SessionKey(session), out LastRun value);
		return JsonSerializer.Serialize(new
		{
			schema = "script_scene/v0",
			ok = true,
			scene = "script",
			pulse = ((!flag) ? "no project — cdp_open first" : ((array.Length == 0) ? "scripts dir ready — put a .csx" : $"{array.Length} script(s)")),
			scripts_root = text,
			scripts = array,
			last = (((object)value == null) ? null : new { value.Path, value.Mode, value.Ok, value.AtUtc }),
			kinds = new object[2]
			{
				new
				{
					id = "csx",
					title = "CSX (ScriptGlobals)",
					status = "live"
				},
				new
				{
					id = "yaml",
					title = "YAML plans",
					status = "planned"
				}
			},
			next = (flag ? Next(("script_put", "Put script", "name= + text= → .cdp/scripts/*.csx"), ("script_check", "Check", "allowlist compile + anchors"), ("script_run", "Run", "path= or open buffer"), ("script_last", "Last report", "previous check/run")) : Next(("project_scene", "Open project", "cdp_open first"))),
			hint = "Habitat: put → edit buffer (diagnostics) → check → run → report. Not put-and-pray. Scripts under .cdp/scripts/. YAML kind later."
		}, Pretty);
	}

	private static async Task<string> PutAsync(DocumentBufferStore store, SessionContext session, IReadOnlyDictionary<string, ICdpBackendModule> byDomain, IReadOnlyDictionary<string, JsonElement> args, CancellationToken ct)
	{
		string projectRoot = session.ProjectRoot;
		if (projectRoot == null || projectRoot.Length <= 0)
		{
			return Err("no_project", "put", "cdp_open first");
		}
		string text = Path.GetFileName((OptString(args, "name") ?? OptString(args, "file") ?? "script").Trim());
		if (!text.EndsWith(".csx", StringComparison.OrdinalIgnoreCase))
		{
			text += ".csx";
		}
		string text2 = ScriptsRoot(session);
		Directory.CreateDirectory(text2);
		string full = Path.Combine(text2, text);
		bool flag = BoolOr(args, "overwrite", File.Exists(full));
		if (File.Exists(full) && !flag)
		{
			return JsonSerializer.Serialize(new
			{
				schema = "script_scene/v0",
				ok = false,
				op = "put",
				error = "file_exists",
				path = full,
				hint = "overwrite=true or pick another name="
			}, Pretty);
		}
		string text3 = OptString(args, "text") ?? OptString(args, "body") ?? OptString(args, "code") ?? "// CDP script — edit in buffer, then go=script_check / script_run\nawait Help.Of(\"Symbol\");";
		DocBuffer buf = store.Create(full, text3.Replace("\r\n", "\n"), overwrite: true);
		string wire = RelWire(session, full);
		object diagnostics = await TryBufferDiagnosticsAsync(store, session, byDomain, full, ct).ConfigureAwait(continueOnCapturedContext: false);
		return JsonSerializer.Serialize(new
		{
			schema = "script_scene/v0",
			ok = true,
			op = "put",
			path = full,
			anchor = wire,
			meta = buf.ToMeta(),
			diagnostics = diagnostics,
			land = new
			{
				anchor = wire,
				doc_id = buf.DocId,
				start_line = 1,
				end_line = Math.Min(12, LineCount(buf.Text)),
				text = string.Join("\n", buf.Text.Replace("\r\n", "\n").Split('\n').Take(12))
			},
			next = Next(("edit_draft", "Edit in IDE", "diagnostics on buffer — not pray"), ("script_check", "Check CSX", "allowlist compile"), ("script_run", "Run", "after green check"), ("diagnostics", "Buffer diagnostics", "syntax on open buffer")),
			hint = "Draft in .cdp/scripts. Refine with buffer edit/diagnostics, then check/run."
		}, Pretty);
	}

	private static async Task<string> OpenAsync(DocumentBufferStore store, SessionContext session, IReadOnlyDictionary<string, ICdpBackendModule> byDomain, IReadOnlyDictionary<string, JsonElement> args, CancellationToken ct)
	{
		if (!TryResolveScriptPath(store, session, args, out string full, out string error))
		{
			return Err(error, "open", "path= / name= under .cdp/scripts or open buffer");
		}
		DocBuffer buf = store.Open(full, BoolOr(args, "refresh", fallback: false));
		object diagnostics = await TryBufferDiagnosticsAsync(store, session, byDomain, full, ct).ConfigureAwait(continueOnCapturedContext: false);
		return JsonSerializer.Serialize(new
		{
			schema = "script_scene/v0",
			ok = true,
			op = "open",
			path = full,
			anchor = RelWire(session, full),
			meta = buf.ToMeta(),
			diagnostics = diagnostics,
			next = Next(("edit_draft", "Edit", "buffer ready"), ("script_check", "Check", "compile"), ("script_run", "Run", "execute")),
			hint = "Opened in buffer — Instant Save on edit; diagnostics in-result."
		}, Pretty);
	}

	private static async Task<string> CheckAsync(DocumentBufferStore store, SessionContext session, IReadOnlyDictionary<string, ICdpBackendModule> byDomain, IReadOnlyDictionary<string, JsonElement> args, CancellationToken ct)
	{
		if (!TryResolveScriptPath(store, session, args, out string full, out string error))
		{
			return Err(error, "check", null);
		}
		FlushIfOpen(store, full);
		ScriptReport report = await ScriptHost.CheckAsync(await File.ReadAllTextAsync(full, ct).ConfigureAwait(continueOnCapturedContext: false), ct).ConfigureAwait(continueOnCapturedContext: false);
		string rel = Rel(session, full);
		var anchors = (report.DiagnosticItems ?? Array.Empty<CsxDiagnosticProjection.Item>()).Select(delegate(CsxDiagnosticProjection.Item d)
		{
			string anchor = d.Anchor;
			object anchor2;
			if (anchor == null || anchor.Length <= 0)
			{
				int? line = d.Line;
				if (line.HasValue)
				{
					int valueOrDefault = line.GetValueOrDefault();
					anchor2 = $"[F:{rel}; L:{valueOrDefault}]";
				}
				else
				{
					anchor2 = RelWire(session, full);
				}
			}
			else
			{
				anchor2 = d.Anchor.Replace("<csx>", rel, StringComparison.Ordinal);
			}
			return new
			{
				anchor = (string)anchor2,
				Line = d.Line,
				Column = d.Column,
				severity = d.Severity,
				id = d.Id,
				message = d.Message,
				hint = d.Hint
			};
		}).ToArray();
		object buffer_diagnostics = await TryBufferDiagnosticsAsync(store, session, byDomain, full, ct).ConfigureAwait(continueOnCapturedContext: false);
		string bodyJson = JsonSerializer.Serialize(new
		{
			ok = report.Ok,
			op = "check",
			path = full,
			diagnostics = anchors,
			count = anchors.Length
		});
		Remember(session, full, "check", report.Ok, bodyJson, report.Ok ? ("check ok · " + Path.GetFileName(full)) : $"check FAIL · {anchors.Length} diag · {Path.GetFileName(full)}");
		return JsonSerializer.Serialize(new
		{
			schema = "script_scene/v0",
			ok = report.Ok,
			op = "check",
			path = full,
			anchor = RelWire(session, full),
			diagnostics = anchors,
			count = anchors.Length,
			buffer_diagnostics = buffer_diagnostics,
			next = (report.Ok ? Next(("script_run", "Run", "check green"), ("report", "Report board", "go=report"), ("edit_draft", "Tweak", "still in buffer")) : Next(("report", "Report board", "go=report"), ("peek", "Peek error", "wire= from diagnostics[].anchor"), ("edit_draft", "Fix in IDE", "then check again"), ("script_check", "Re-check", "after edit"))),
			hint = "CSX allowlist compile. Fix via buffer — go=report for evidence board."
		}, Pretty);
	}

	private static async Task<string> RunAsync(DocumentBufferStore store, SessionContext session, IReadOnlyDictionary<string, JsonElement> args, Func<string, IReadOnlyDictionary<string, JsonElement>, CancellationToken, Task<string>> metaDispatch, CancellationToken ct)
	{
		if (!TryResolveScriptPath(store, session, args, out string full, out string error))
		{
			return Err(error, "run", null);
		}
		FlushIfOpen(store, full);
		string mode = (OptString(args, "mode") ?? "run").Trim();
		if (string.Equals(OptString(args, "op"), "dry_run", StringComparison.OrdinalIgnoreCase) || string.Equals(mode, "dryrun", StringComparison.OrdinalIgnoreCase))
		{
			mode = "dry_run";
		}
		Dictionary<string, JsonElement> dictionary = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			["path"] = JsonSerializer.SerializeToElement(full),
			["mode"] = JsonSerializer.SerializeToElement(mode)
		};
		string projectRoot = session.ProjectRoot;
		if (projectRoot != null && projectRoot.Length > 0)
		{
			dictionary["workspace_path"] = JsonSerializer.SerializeToElement(projectRoot);
		}
		string text;
		try
		{
			text = await metaDispatch("cdp_csx_run", dictionary, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			Remember(session, full, mode, ok: false, ex.Message, "run FAIL · " + Path.GetFileName(full));
			return JsonSerializer.Serialize(new
			{
				schema = "script_scene/v0",
				ok = false,
				op = "run",
				path = full,
				error = "run_failed",
				message = ex.Message
			}, Pretty);
		}
		bool ok = true;
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(text);
			if (jsonDocument.RootElement.TryGetProperty("ok", out var value))
			{
				ok = value.ValueKind != JsonValueKind.False;
			}
		}
		catch
		{
		}
		Remember(session, full, mode, ok, text);
		return JsonSerializer.Serialize(new
		{
			schema = "script_scene/v0",
			ok = ok,
			op = "run",
			mode = mode,
			path = full,
			anchor = RelWire(session, full),
			report = JsonSerializer.Deserialize<JsonElement>(text),
			next = Next(("report", "Report board", "go=report · PFD evidence"), ("script_last", "Last meta", "stored last"), ("script_check", "Re-check", "if failed"), ("edit_draft", "Edit", "iterate in buffer"), ("script_run", "Rerun", "same path")),
			hint = "Ran in session. go=report for evidence board — not put-and-pray."
		}, Pretty);
	}

	private static string Last(SessionContext session)
	{
		if (!LastByRoot.TryGetValue(SessionKey(session), out LastRun value))
		{
			return Err("no_last_run", "last", "check or run first");
		}
		return JsonSerializer.Serialize(new
		{
			schema = "script_scene/v0",
			ok = value.Ok,
			op = "last",
			path = value.Path,
			mode = value.Mode,
			succeeded = value.Ok,
			at_utc = value.AtUtc,
			pulse = value.Pulse,
			board = value.Board,
			body = TryParseBody(value.BodyJson),
			next = Next(("report", "Report board", "go=report"), ("script_run", "Rerun", "same script"), ("script_open", "Open", "back to buffer")),
			hint = "Last check/run — go=report for sit/report channel (ADR 0193)."
		}, Pretty);
	}

	private static string Help(IReadOnlyDictionary<string, JsonElement> args)
	{
		string text = OptString(args, "path") ?? OptString(args, "of");
		if (!string.IsNullOrWhiteSpace(text))
		{
			return CsxHelpCatalog.Of(text);
		}
		return CsxHelpCatalog.Toc();
	}

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
