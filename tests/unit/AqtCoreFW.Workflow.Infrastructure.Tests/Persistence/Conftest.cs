using AqtCoreFW.Tests.SharedFixtures;
using Xunit;

namespace AqtCoreFW.Workflow.Infrastructure.Tests.Persistence;

/// <summary>
/// Collection fixture definition for SqlServer — allows tests in this assembly to use SqlServerFixture.
/// Matches the collection name "SqlServer" used in test classes.
/// </summary>
[CollectionDefinition(SqlServerCollection.CollectionName)]
public sealed class SqlServerCollectionDefinition : ICollectionFixture<SqlServerFixture>
{
}
