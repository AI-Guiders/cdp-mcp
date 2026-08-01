#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Run/last/help ops for Ps1Scene (pwsh + path helpers in peels).</summary>
internal static partial class Ps1Scene
{
	private static async Task<string> RunAsync(
		DocumentBufferStore store,
		SessionContext session,
		IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
		IReadOnlyDictionary<string, JsonElement> args,
		CancellationToken ct)
	{
		if (!TryResolveScriptPath(store, session, args, out var full, out var error))
			return Err(error, "run", null);
		FlushIfOpen(store, full);

		var mode = (OptString(args, "mode") ?? "run").Trim();
		if (string.Equals(OptString(args, "op"), "dry_run", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(mode, "dryrun", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(mode, "dry_run", StringComparison.OrdinalIgnoreCase))
		{
			var checkArgs = new Dictionary<string, JsonElement>(args, StringComparer.Ordinal)
			{
				["op"] = JsonSerializer.SerializeToElement("check")
			};
			return await CheckAsync(store, session, byDomain, checkArgs, ct).ConfigureAwait(false);
		}

		var pwsh = ResolvePwsh();
		if (pwsh is null)
			return Err("pwsh_missing", "run", "Install PowerShell 7+ (pwsh) on PATH");

		var timeout = 120;
		if (args.TryGetValue("timeout_seconds", out var tEl) && tEl.TryGetInt32(out var t) && t > 0)
			timeout = t;

		var cwd = session.ProjectRoot ?? Path.GetDirectoryName(full)!;
		var (exit, stdout, stderr, ms) = await RunPwshAsync(pwsh, ["-NoProfile", "-File", full], cwd, timeout, ct).ConfigureAwait(false);
		var ok = exit == 0;
		var bodyJson = JsonSerializer.Serialize(new
		{
			ok,
			op = "run",
			path = full,
			exit_code = exit,
			stdout,
			stderr,
			elapsed_ms = ms,
			cwd,
			exe = pwsh
		});
		Remember(session, full, mode, ok, bodyJson, ok ? "run ok · " + Path.GetFileName(full) : $"run FAIL · exit {exit} · {Path.GetFileName(full)}", exit);
		return JsonSerializer.Serialize(new
		{
			schema = Schema,
			ok,
			op = "run",
			mode,
			path = full,
			anchor = RelWire(session, full),
			exit_code = exit,
			elapsed_ms = ms,
			cwd,
			exe = pwsh,
			stdout = CapText(stdout, BodyCapChars),
			stderr = CapText(stderr, 4000),
			next = Next(
				("ps1_last", "Last meta", "stored last"),
				("ps1_check", "Re-check", "if failed"),
				("edit_draft", "Edit", "iterate in buffer"),
				("ps1_run", "Rerun", "same path")),
			hint = "Ran via pwsh -NoProfile -File. Result board in stdout/stderr — ISE output pane analogue."
		}, Pretty);
	}

	private static string Last(SessionContext session)
	{
		if (!LastByRoot.TryGetValue(SessionKey(session), out var last))
			return Err("no_last_run", "last", "check or run first");
		return JsonSerializer.Serialize(new
		{
			schema = Schema,
			ok = last.Ok,
			op = "last",
			path = last.Path,
			mode = last.Mode,
			succeeded = last.Ok,
			at_utc = last.AtUtc,
			pulse = last.Pulse,
			exit_code = last.ExitCode,
			body = TryParseBody(last.BodyJson),
			next = Next(("ps1_run", "Rerun", "same script"), ("ps1_open", "Open", "back to buffer"), ("ps1_check", "Check", "AST")),
			hint = "Last check/run — ISE output history analogue."
		}, Pretty);
	}

	private static string Help() => JsonSerializer.Serialize(new
	{
		schema = Schema,
		ok = true,
		op = "help",
		pulse = "ps1_scene · ISE analogue",
		ops = new[]
		{
			new { op = "scene", why = "map .cdp/ps1 + last" },
			new { op = "put", why = "name= + text= → .cdp/ps1/*.ps1 + buffer" },
			new { op = "open", why = "path=/name= into buffer" },
			new { op = "check", why = "AST parse (no execute)" },
			new { op = "run", why = "pwsh -NoProfile -File; dry_run=check" },
			new { op = "last", why = "previous check/run body" },
			new { op = "help", why = "this" }
		},
		hint = "Mirror of cdp_script_scene for PowerShell. Habitat axis of CDP full-ready."
	}, Pretty);
}
