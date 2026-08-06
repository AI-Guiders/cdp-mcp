#nullable enable
using Xunit;

namespace CdpMcp.Tests;

/// <summary>Serial — Voice/Identity/Presence RootOverrideForTests is process-static.</summary>
[CollectionDefinition(nameof(IntercomLatchSerial), DisableParallelization = true)]
public sealed class IntercomLatchSerial;
