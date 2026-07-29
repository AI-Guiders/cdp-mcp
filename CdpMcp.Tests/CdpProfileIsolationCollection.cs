#nullable enable
using Xunit;

namespace CdpMcp.Tests;

/// <summary>Serialize tests that mutate global <c>CdpProfile.ApplyClientRoots</c>.</summary>
[CollectionDefinition("CdpProfileIsolation", DisableParallelization = true)]
public sealed class CdpProfileIsolationCollection;
