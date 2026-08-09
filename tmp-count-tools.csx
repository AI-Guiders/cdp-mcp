#nullable enable
using CdpMcp;
var tools = MetaToolCatalog.Build();
Console.WriteLine("meta=" + tools.Count);
foreach (var t in tools.OrderBy(x => x.Name).Take(5)) Console.WriteLine(t.Name);
Console.WriteLine("...");
foreach (var t in tools.OrderBy(x => x.Name).TakeLast(5)) Console.WriteLine(t.Name);
