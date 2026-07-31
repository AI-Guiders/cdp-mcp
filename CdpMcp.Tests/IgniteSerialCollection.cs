using Xunit;

namespace CdpMcp.Tests;

/// <summary>Serialize ignite host tests — shared Arms store / autonomous+HILD disk latches.</summary>
[CollectionDefinition("IgniteSerial")]
public sealed class IgniteSerialCollection : ICollectionFixture<object>
{
}
