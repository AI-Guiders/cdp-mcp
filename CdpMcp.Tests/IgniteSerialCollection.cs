using Xunit;

namespace CdpMcp.Tests;

/// <summary>Serialize ignite host tests — shared Arms store / autonomous+HILD disk latches.</summary>
[CollectionDefinition("IgniteSerial", DisableParallelization = true)]
public sealed class IgniteSerialCollection : ICollectionFixture<object>
{
}
