#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Run op for ScriptScene (metaDispatch cdp_csx_run).</summary>
internal static partial class ScriptScene
{
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
}
