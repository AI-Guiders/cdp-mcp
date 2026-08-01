#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Path / JSON / last-run helpers for Ps1Scene.</summary>
internal static partial class Ps1Scene
{
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
