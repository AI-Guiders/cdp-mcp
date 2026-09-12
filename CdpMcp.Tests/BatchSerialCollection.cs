using Xunit;

namespace CdpMcp.Tests;

/// <summary>Serialize batch-polluted tests — shared static state across Citizen/Ignite/Glass/Mcp boundaries.</summary>
[CollectionDefinition("BatchSerial", DisableParallelization = true)]
public sealed class BatchSerialCollection : ICollectionFixture<object>
{
}
