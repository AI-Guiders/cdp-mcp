#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cdp.Core;

namespace CdpMcp;

/// <summary>PowerShell ISE-analogue habitat: put → buffer → check (AST) → run (pwsh -File) → last.</summary>
internal static partial class Ps1Scene
{
	public sealed record LastRun(string Path, string Mode, bool Ok, DateTime AtUtc, string Pulse, string? BodyJson, int? ExitCode);

	public const string Schema = "ps1_scene/v0";
	public const string ToolName = "cdp_ps1_scene";

	private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
	private static readonly ConcurrentDictionary<string, LastRun> LastByRoot = new(StringComparer.OrdinalIgnoreCase);
	private const int BodyCapChars = 12000;

	public static bool IsPs1Tool(string name) =>
		string.Equals(name, ToolName, StringComparison.OrdinalIgnoreCase);

	public static (bool Ok, string Pulse) Pulse(SessionContext session)
	{
		if (session.ProjectRoot is not { Length: > 0 })
			return (true, "no project — cdp_open");
		var n = ListScripts(ScriptsRoot(session)).Length;
		return (true, n == 0 ? "ps1 ready — put" : $"{n} ps1");
	}

	/// <summary>SoftBoard / seat pulse object (not JSON string).</summary>
	public static object Board(SessionContext session)
	{
		var root = ScriptsRoot(session);
		var hasProject = session.ProjectRoot is { Length: > 0 };
		var scripts = ListScripts(root);
		LastByRoot.TryGetValue(SessionKey(session), out var last);
		return new
		{
			schema = Schema,
			ok = true,
			scene = "ps1",
			pulse = !hasProject
				? "no project — cdp_open first"
				: scripts.Length == 0
					? "ps1 dir ready — put a .ps1"
					: $"{scripts.Length} ps1 script(s)",
			scripts_root = root,
			scripts,
			last = last is null ? null : new { last.Path, last.Mode, last.Ok, last.AtUtc, last.ExitCode },
			hint = "PS ISE habitat: put → edit buffer → check (AST) → run → last."
		};
	}

	public static async Task<string> DispatchAsync(
		DocumentBufferStore store,
		SessionContext session,
		IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
		IReadOnlyDictionary<string, JsonElement> args,
		CancellationToken ct = default)
	{
		var op = (OptString(args, "op") ?? OptString(args, "feature") ?? "scene").Trim().ToLowerInvariant();
		return op switch
		{
			"scene" or "map" or "status" or "" => SceneMap(session),
			"put" or "new" or "create" => await PutAsync(store, session, byDomain, args, ct).ConfigureAwait(false),
			"open" => await OpenAsync(store, session, byDomain, args, ct).ConfigureAwait(false),
			"check" or "parse" or "compile" => await CheckAsync(store, session, byDomain, args, ct).ConfigureAwait(false),
			"run" or "dryrun" or "dry_run" => await RunAsync(store, session, byDomain, args, ct).ConfigureAwait(false),
			"report" or "last" => Last(session),
			"help" => Help(),
			_ => Err("unknown_op", op, "op=scene|put|open|check|run|last|help")
		};
	}

	private static string SceneMap(SessionContext session)
	{
		var root = ScriptsRoot(session);
		var hasProject = session.ProjectRoot is { Length: > 0 };
		var scripts = ListScripts(root);
		LastByRoot.TryGetValue(SessionKey(session), out var last);
		return JsonSerializer.Serialize(new
		{
			schema = Schema,
			ok = true,
			scene = "ps1",
			pulse = !hasProject
				? "no project — cdp_open first"
				: scripts.Length == 0
					? "ps1 dir ready — put a .ps1"
					: $"{scripts.Length} ps1 script(s)",
			scripts_root = root,
			scripts,
			last = last is null ? null : new { last.Path, last.Mode, last.Ok, last.AtUtc, last.ExitCode },
			kinds = new object[]
			{
				new { id = "ps1", title = "PowerShell (pwsh -File)", status = "live" },
				new { id = "ise", title = "ISE analogue: editor + AST check + run result", status = "live" }
			},
			next = hasProject
				? Next(
					("ps1_put", "Put script", "name= + text= → .cdp/ps1/*.ps1"),
					("ps1_check", "Check", "AST parse via pwsh"),
					("ps1_run", "Run", "pwsh -NoProfile -File"),
					("ps1_last", "Last report", "previous check/run"))
				: Next(("project_scene", "Open project", "cdp_open first")),
			hint = "PS ISE habitat: put → edit buffer → check (AST) → run → last. Scripts under .cdp/ps1/. Not Avalonia ISE clone."
		}, Pretty);
	}

	private static async Task<string> PutAsync(
		DocumentBufferStore store,
		SessionContext session,
		IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
		IReadOnlyDictionary<string, JsonElement> args,
		CancellationToken ct)
	{
		if (session.ProjectRoot is not { Length: > 0 })
			return Err("no_project", "put", "cdp_open first");

		var name = Path.GetFileName((OptString(args, "name") ?? OptString(args, "file") ?? "script").Trim());
		if (!name.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
			name += ".ps1";

		var dir = ScriptsRoot(session)!;
		Directory.CreateDirectory(dir);
		var full = Path.Combine(dir, name);
		var overwrite = BoolOr(args, "overwrite", File.Exists(full));
		if (File.Exists(full) && !overwrite)
		{
			return JsonSerializer.Serialize(new
			{
				schema = Schema,
				ok = false,
				op = "put",
				error = "file_exists",
				path = full,
				hint = "overwrite=true or pick another name="
			}, Pretty);
		}

		var body = OptString(args, "text") ?? OptString(args, "body") ?? OptString(args, "code")
			?? "# CDP PS1 — edit in buffer, then go=ps1_check / ps1_run\nWrite-Host 'hello from ps1_scene'\n";
		var buf = store.Create(full, body.Replace("\r\n", "\n"), overwrite: true);
		var wire = RelWire(session, full);
		var diagnostics = await TryBufferDiagnosticsAsync(store, session, byDomain, full, ct).ConfigureAwait(false);
		return JsonSerializer.Serialize(new
		{
			schema = Schema,
			ok = true,
			op = "put",
			path = full,
			anchor = wire,
			meta = buf.ToMeta(),
			diagnostics,
			land = new
			{
				anchor = wire,
				doc_id = buf.DocId,
				start_line = 1,
				end_line = Math.Min(12, LineCount(buf.Text)),
				text = string.Join("\n", buf.Text.Replace("\r\n", "\n").Split('\n').Take(12))
			},
			next = Next(
				("edit_draft", "Edit in IDE", "buffer diagnostics"),
				("ps1_check", "Check AST", "Parser.ParseFile"),
				("ps1_run", "Run", "after green check"),
				("diagnostics", "Buffer diagnostics", "syntax on open buffer")),
			hint = "Draft in .cdp/ps1. Refine in buffer, then check/run."
		}, Pretty);
	}

	private static async Task<string> OpenAsync(
		DocumentBufferStore store,
		SessionContext session,
		IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
		IReadOnlyDictionary<string, JsonElement> args,
		CancellationToken ct)
	{
		if (!TryResolveScriptPath(store, session, args, out var full, out var error))
			return Err(error, "open", "path= / name= under .cdp/ps1 or open .ps1 buffer");
		var buf = store.Open(full, BoolOr(args, "refresh", fallback: false));
		var diagnostics = await TryBufferDiagnosticsAsync(store, session, byDomain, full, ct).ConfigureAwait(false);
		return JsonSerializer.Serialize(new
		{
			schema = Schema,
			ok = true,
			op = "open",
			path = full,
			anchor = RelWire(session, full),
			meta = buf.ToMeta(),
			diagnostics,
			next = Next(("edit_draft", "Edit", "buffer ready"), ("ps1_check", "Check", "AST"), ("ps1_run", "Run", "execute")),
			hint = "Opened in buffer — Instant Save on edit."
		}, Pretty);
	}

	private static async Task<string> CheckAsync(
		DocumentBufferStore store,
		SessionContext session,
		IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
		IReadOnlyDictionary<string, JsonElement> args,
		CancellationToken ct)
	{
		if (!TryResolveScriptPath(store, session, args, out var full, out var error))
			return Err(error, "check", null);
		FlushIfOpen(store, full);

		var pwsh = ResolvePwsh();
		if (pwsh is null)
			return Err("pwsh_missing", "check", "Install PowerShell 7+ (pwsh) on PATH");

		var escaped = full.Replace("'", "''");
		var cmd =
			"$e=$null;$t=$null;[void][System.Management.Automation.Language.Parser]::ParseFile('" +
			escaped +
			"',[ref]$t,[ref]$e); if($e -and $e.Count -gt 0){ $e | ForEach-Object { $_.ToString() }; exit 1 } else { 'ast ok' }";
		var (exit, stdout, stderr, ms) = await RunPwshAsync(pwsh, ["-NoProfile", "-Command", cmd], session.ProjectRoot ?? Path.GetDirectoryName(full)!, 45, ct)
			.ConfigureAwait(false);
		var ok = exit == 0;
		var board = CapText(string.IsNullOrWhiteSpace(stdout) ? stderr : stdout, BodyCapChars);
		var bodyJson = JsonSerializer.Serialize(new { ok, op = "check", path = full, exit_code = exit, stdout, stderr, elapsed_ms = ms });
		Remember(session, full, "check", ok, bodyJson, ok ? "check ok · " + Path.GetFileName(full) : $"check FAIL · {Path.GetFileName(full)}", exit);
		var buffer_diagnostics = await TryBufferDiagnosticsAsync(store, session, byDomain, full, ct).ConfigureAwait(false);
		return JsonSerializer.Serialize(new
		{
			schema = Schema,
			ok,
			op = "check",
			path = full,
			anchor = RelWire(session, full),
			exit_code = exit,
			elapsed_ms = ms,
			board,
			stdout = CapText(stdout, 4000),
			stderr = CapText(stderr, 2000),
			buffer_diagnostics,
			next = ok
				? Next(("ps1_run", "Run", "check green"), ("ps1_last", "Last", "stored"), ("edit_draft", "Tweak", "buffer"))
				: Next(("edit_draft", "Fix in IDE", "then check again"), ("ps1_check", "Re-check", "after edit"), ("ps1_last", "Last", "board")),
			hint = "AST parse via System.Management.Automation.Language.Parser — ISE-style syntax check without executing."
		}, Pretty);
	}
}
