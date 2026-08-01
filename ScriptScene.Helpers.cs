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

/// <summary>Path resolve, remember/board helpers for ScriptScene (FlattenJson peeled).</summary>
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

}
