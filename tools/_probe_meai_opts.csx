#nullable enable
using System;
using Microsoft.Extensions.AI;
using Microsoft.Agents.AI;
Console.WriteLine(""AllowMultiple="" + typeof(ChatOptions).GetProperty(""AllowMultipleToolCalls"")?.PropertyType);
Console.WriteLine(""Instructions="" + typeof(ChatOptions).GetProperty(""Instructions"")?.PropertyType);
Console.WriteLine(""Tools="" + typeof(ChatOptions).GetProperty(""Tools"")?.PropertyType);
foreach (var p in typeof(ChatClientAgentOptions).GetProperties()) Console.WriteLine(""Opt "" + p.Name + "" "" + p.PropertyType.Name);
foreach (var m in typeof(ChatClientExtensions).GetMethods().Where(x => x.Name == ""AsAIAgent""))
  Console.WriteLine(m);
