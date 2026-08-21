using CdpMcp;
using TerminalMcp.Core;

IdeSeatProcessReclaim.Ensure();

var notifyIdx = Array.IndexOf(args, "--ignite-notify");
if (notifyIdx >= 0)
    Environment.Exit(await IdeIgniteNotifyCli.RunAsync(args));

var durableJobIdx = Array.IndexOf(args, "--durable-job");
if (durableJobIdx >= 0 && durableJobIdx + 1 < args.Length)
{
    if (DurableJobStore.TryReadRecordPublic(args[durableJobIdx + 1], out var durableRec)
        && durableRec.Lifecycle is { } life)
        IdeDurableJobRunner.ApplyIgniteSeat(life);
    IdeIgniteArmHost.EnsureStarted();
    Environment.Exit(await IdeDurableJobRunner.RunAsync(args[durableJobIdx + 1]));
}

var configPath = args.SkipWhile(a => a != "--config").Skip(1).FirstOrDefault()
    ?? Environment.GetEnvironmentVariable("CDP_MCP_CONFIG")
    ?? Path.Combine(AppContext.BaseDirectory, "config", "cdp-mcp.toml");

if (args.Contains("--service"))
{
    Environment.Exit(await CdpServiceHost.RunAsync(configPath, args));
}

await using var runtime = await CdpHostRuntime.CreateAsync(configPath);
await runtime.RunStdioAsync();
