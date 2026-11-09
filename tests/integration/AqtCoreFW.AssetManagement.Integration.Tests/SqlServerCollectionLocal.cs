using AqtCoreFW.Tests.SharedFixtures;

namespace AqtCoreFW.AssetManagement.Integration.Tests;

// xUnit discovers collection definitions only in the executing test assembly.
[CollectionDefinition(SqlServerCollection.CollectionName)]
public sealed class SqlServerCollectionLocal : ICollectionFixture<SqlServerFixture> { }
