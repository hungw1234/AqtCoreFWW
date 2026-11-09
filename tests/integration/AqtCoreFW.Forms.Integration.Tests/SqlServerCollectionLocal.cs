using AqtCoreFW.Tests.SharedFixtures;

namespace AqtCoreFW.Forms.Integration.Tests;

// xUnit v2 discovers [CollectionDefinition] only in the executing test assembly.
// SqlServerCollection lives in AqtCoreFW.Tests.SharedFixtures.dll — not scanned by xUnit
// at runtime. Re-declaring it here makes SqlServerFixture available as a collection fixture,
// so WebAppFixture's constructor receives SqlServerFixture via xUnit's collection→class-fixture DI.
[CollectionDefinition(SqlServerCollection.CollectionName)]
public sealed class SqlServerCollectionLocal : ICollectionFixture<SqlServerFixture> { }
