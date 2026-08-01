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

/// <summary>Check op for ScriptScene (CSX allowlist compile).</summary>
internal static partial class ScriptScene
{
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
}
