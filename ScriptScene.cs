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

internal static partial class ScriptScene
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
}
