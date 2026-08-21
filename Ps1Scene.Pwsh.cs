#nullable enable

namespace CdpMcp;

/// <summary>pwsh resolve + process exec for Ps1Scene.</summary>
internal static partial class Ps1Scene
{
	private static Task<(int Exit, string Stdout, string Stderr, int Ms)> RunPwshAsync(
		string exe,
		IReadOnlyList<string> argv,
		string cwd,
		int timeoutSec,
		CancellationToken ct) =>
		Ps1PwshRuntime.RunAsync(exe, argv, cwd, timeoutSec, ct);

	private static string? ResolvePwsh() => Ps1PwshRuntime.Resolve();
}
