#nullable enable

namespace CdpDeskTui;

static class Program
{
    static int Main(string[] args)
    {
        // --seat=all|p|f|m  → one process per monitor later
        string? seat = null;
        foreach (var a in args)
        {
            if (a.StartsWith("--seat=", StringComparison.OrdinalIgnoreCase))
                seat = a["--seat=".Length..];
            else if (a is "-h" or "--help")
            {
                Console.WriteLine("""
                    cdp-desk-tui — Terminal.Gui desk spike (P|F|M)
                      --seat=all   three columns (default)
                      --seat=p|f|m single one seat (multi-monitor = N processes)
                      r            refresh fixture
                      q / Ctrl+Q   quit
                    """);
                return 0;
            }
        }

        return DeskShell.Run(seat);
    }
}
