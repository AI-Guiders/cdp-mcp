#nullable enable
using System;
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

/// <summary>Run/last/help, pwsh exec, path helpers for Ps1Scene.</summary>
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

	private static async Task<(int Exit, string Stdout, string Stderr, int Ms)> RunPwshAsync(
		string exe,
		IReadOnlyList<string> argv,
		string cwd,
		int timeoutSec,
		CancellationToken ct)
	{
		var psi = new ProcessStartInfo
		{
			FileName = exe,
			WorkingDirectory = cwd,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true
		};
		foreach (var a in argv)
			psi.ArgumentList.Add(a);

		var sw = Stopwatch.StartNew();
		using var proc = new Process { StartInfo = psi };
		var stdout = new StringBuilder();
		var stderr = new StringBuilder();
		proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
		proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

		try
		{
			if (!proc.Start())
				return (-1, "", $"failed to start {exe}", (int)sw.ElapsedMilliseconds);
		}
		catch (Exception ex)
		{
			return (-1, "", ex.Message, (int)sw.ElapsedMilliseconds);
		}

		proc.BeginOutputReadLine();
		proc.BeginErrorReadLine();
		using var reg = ct.Register(() =>
		{
			try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
		});

		var finished = await Task.Run(() => proc.WaitForExit(timeoutSec * 1000), ct).ConfigureAwait(false);
		if (!finished)
		{
			try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
			return (-1, CapText(stdout.ToString(), BodyCapChars) ?? "", $"timed out after {timeoutSec}s", (int)sw.ElapsedMilliseconds);
		}

		return (proc.ExitCode, stdout.ToString(), stderr.ToString(), (int)sw.ElapsedMilliseconds);
	}

	private static string? _pwshCached;
	private static bool _pwshResolved;

	private static string? ResolvePwsh()
	{
		if (_pwshResolved) return _pwshCached;
		foreach (var candidate in new[] { "pwsh", "pwsh.exe", "powershell", "powershell.exe" })
		{
			try
			{
				var psi = new ProcessStartInfo
				{
					FileName = candidate,
					ArgumentList = { "-NoProfile", "-Command", "$PSVersionTable.PSVersion.ToString()" },
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					UseShellExecute = false,
					CreateNoWindow = true
				};
				using var p = Process.Start(psi);
				if (p is null) continue;
				if (!p.WaitForExit(5000)) { try { p.Kill(true); } catch { } continue; }
				if (p.ExitCode == 0)
				{
					_pwshCached = candidate;
					_pwshResolved = true;
					return candidate;
				}
			}
			catch { /* try next */ }
		}
		_pwshResolved = true;
		_pwshCached = null;
		return null;
	}

	private static async Task<object?> TryBufferDiagnosticsAsync(
		DocumentBufferStore store,
		SessionContext session,
		IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
		string full,
		CancellationToken ct)
	{
		try
		{
			var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				["op"] = JsonSerializer.SerializeToElement("diagnostics"),
				["path"] = JsonSerializer.SerializeToElement(full),
				["force"] = JsonSerializer.SerializeToElement(true)
			};
			return JsonSerializer.Deserialize<JsonElement>(
				await DocumentEditPlane.DispatchAsync("cdp_buffer", store, session, byDomain, args, ct).ConfigureAwait(false));
		}
		catch { return null; }
	}

	private static void FlushIfOpen(DocumentBufferStore store, string full)
	{
		var buf = store.All.FirstOrDefault(b => string.Equals(b.Path, full, StringComparison.OrdinalIgnoreCase));
		if (buf is { Dirty: true })
			store.Flush(buf, allowShrink: true);
	}

	private static bool TryResolveScriptPath(
		DocumentBufferStore store,
		SessionContext session,
		IReadOnlyDictionary<string, JsonElement> args,
		out string full,
		out string error)
	{
		full = "";
		error = "path_required";
		var path = OptString(args, "path") ?? OptString(args, "file");
		var name = OptString(args, "name");
		if (path is { Length: > 0 })
		{
			full = Path.IsPathRooted(path)
				? Path.GetFullPath(path)
				: Path.GetFullPath(Path.Combine(session.ProjectRoot ?? ScriptsRoot(session) ?? ".", path));
			if (!File.Exists(full))
			{
				var under = ScriptsRoot(session);
				if (under is not null)
				{
					var alt = Path.Combine(under, Path.GetFileName(path));
					if (File.Exists(alt)) full = alt;
				}
			}
		}
		else if (name is { Length: > 0 } && ScriptsRoot(session) is { } root)
		{
			var fn = Path.GetFileName(name);
			if (!fn.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)) fn += ".ps1";
			full = Path.Combine(root, fn);
		}
		else
		{
			var open = store.All.FirstOrDefault(b => b.Path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase));
			if (open is not null) full = open.Path;
		}

		if (full.Length == 0 || !File.Exists(full))
		{
			error = full.Length == 0 ? "path_required" : "not_found";
			return false;
		}
		return true;
	}

	private static string? ScriptsRoot(SessionContext session) =>
		session.ProjectRoot is { Length: > 0 } p ? Path.Combine(p, ".cdp", "ps1") : null;

	private static object[] ListScripts(string? root)
	{
		if (root is null || !Directory.Exists(root)) return [];
		return Directory.EnumerateFiles(root, "*.ps1")
			.OrderByDescending(File.GetLastWriteTimeUtc)
			.Take(40)
			.Select(p => (object)new { name = Path.GetFileName(p), path = p, mtime_utc = File.GetLastWriteTimeUtc(p) })
			.ToArray();
	}

	private static void Remember(SessionContext session, string path, string mode, bool ok, string? bodyJson, string pulse, int? exitCode)
	{
		LastByRoot[SessionKey(session)] = new LastRun(path, mode, ok, DateTime.UtcNow, TrimPulse(pulse), CapText(bodyJson, BodyCapChars), exitCode);
	}

	private static string SessionKey(SessionContext session) =>
		session.ProjectRoot is { Length: > 0 } p ? p : "_";

	private static string RelWire(SessionContext session, string full)
	{
		var root = session.ProjectRoot;
		if (root is { Length: > 0 } && full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
		{
			var rel = full[root.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			return rel.Replace('\\', '/');
		}
		return full.Replace('\\', '/');
	}

	private static string Err(string error, string? op, string? hint) => JsonSerializer.Serialize(new
	{
		schema = Schema,
		ok = false,
		op,
		error,
		hint
	}, Pretty);

	private static object[] Next(params (string Go, string Label, string Why)[] items) =>
		items.Select(i => (object)new { go = i.Go, label = i.Label, why = i.Why }).ToArray();

	private static string? OptString(IReadOnlyDictionary<string, JsonElement> args, string key)
	{
		if (!args.TryGetValue(key, out var el)) return null;
		return el.ValueKind switch
		{
			JsonValueKind.String => el.GetString(),
			JsonValueKind.Number => el.ToString(),
			JsonValueKind.True => "true",
			JsonValueKind.False => "false",
			_ => null
		};
	}

	private static bool BoolOr(IReadOnlyDictionary<string, JsonElement> args, string key, bool fallback)
	{
		if (!args.TryGetValue(key, out var el)) return fallback;
		return el.ValueKind switch
		{
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			JsonValueKind.String when bool.TryParse(el.GetString(), out var b) => b,
			_ => fallback
		};
	}

	private static int LineCount(string text)
	{
		if (string.IsNullOrEmpty(text)) return 0;
		var n = 1;
		foreach (var c in text) if (c == '\n') n++;
		return n;
	}

	private static string TrimPulse(string s) => s.Length <= 96 ? s : s[..95] + "…";

	private static string? CapText(string? text, int max)
	{
		if (text is null) return null;
		return text.Length <= max ? text : text[..max] + "…";
	}

	private static object? TryParseBody(string? json)
	{
		if (string.IsNullOrWhiteSpace(json)) return null;
		try { return JsonSerializer.Deserialize<JsonElement>(json); }
		catch { return json; }
	}
}
