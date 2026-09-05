using CdpMcp;
using TerminalMcp.Core;

var notifyIdx = Array.IndexOf(args, "--ignite-notify");
var durableJobIdx = Array.IndexOf(args, "--durable-job");
var gatekeeperIdx = Array.IndexOf(args, "--gatekeeper");
if (gatekeeperIdx < 0 && notifyIdx < 0 && durableJobIdx < 0)
    IdeSeatProcessReclaim.Ensure();
else
    Environment.SetEnvironmentVariable(IdeSeatProcessReclaim.SkipEnv, "1");



if (notifyIdx >= 0)
    Environment.Exit(await IdeIgniteNotifyCli.RunAsync(args));


if (durableJobIdx >= 0 && durableJobIdx + 1 < args.Length)
{
    if (DurableJobStore.TryReadRecordPublic(args[durableJobIdx + 1], out var durableRec)
        && durableRec.Lifecycle is { } life)
        IdeDurableJobRunner.ApplyIgniteSeat(life);
    IdeIgniteArmHost.EnsureStarted();
    Environment.Exit(await IdeDurableJobRunner.RunAsync(args[durableJobIdx + 1]));
}

if (gatekeeperIdx >= 0)
    Environment.Exit(await CdpGatekeeperHost.RunAsync());

var deployCliIdx = Array.IndexOf(args, "--deploy-cli");
if (deployCliIdx >= 0 && deployCliIdx + 1 < args.Length)
    Environment.Exit(IdeDeployCli.Run(args[deployCliIdx + 1]));

var configPath = args.SkipWhile(a => a != "--config").Skip(1).FirstOrDefault()
    ?? Environment.GetEnvironmentVariable("CDP_MCP_CONFIG")
    ?? Path.Combine(AppContext.BaseDirectory, "config", "cdp-mcp.toml");

if (args.Contains("--service"))
{
    Environment.Exit(await CdpServiceHost.RunAsync(configPath, args));
}

await using var runtime = await CdpHostRuntime.CreateAsync(configPath);
await runtime.RunStdioAsync();
