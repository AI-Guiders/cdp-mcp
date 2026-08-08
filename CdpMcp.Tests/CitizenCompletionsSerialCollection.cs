#nullable enable
using Xunit;

namespace CdpMcp.Tests;

/// <summary>HTTP stub + cost ledger share statics — run these serially.</summary>
[CollectionDefinition("CitizenCompletionsSerial", DisableParallelization = true)]
public sealed class CitizenCompletionsSerialCollection;
