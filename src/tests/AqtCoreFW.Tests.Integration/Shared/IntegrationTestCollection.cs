using AqtCoreFW.Tests.Integration.Infrastructure;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Shared;

/// <summary>
/// Shared collection fixture — DatabaseFixture is created once and shared across all
/// test classes decorated with [Collection("Integration")].
/// DatabaseFixture owns the container lifetime AND the ApiFactory lifetime.
/// Tests access the factory via DatabaseFixture.Factory (set after migrations complete).
/// </summary>
[CollectionDefinition("Integration")]
public class IntegrationTestCollection : ICollectionFixture<DatabaseFixture>
{
}
