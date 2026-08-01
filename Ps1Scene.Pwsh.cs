#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CdpMcp;

/// <summary>pwsh resolve + process exec for Ps1Scene.</summary>
internal static partial class Ps1Scene
{
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
}
