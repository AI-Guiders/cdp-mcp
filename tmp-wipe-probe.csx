#nullable enable
using Cdp.IntercomJournal;
var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "cdp-mcp");
Console.WriteLine("count=" + IntercomJournalStore.Count(root));
try {
  var n = IntercomJournalStore.WipeAll(root);
  Console.WriteLine("wipe=" + n);
} catch (Exception ex) { Console.WriteLine(ex); }
