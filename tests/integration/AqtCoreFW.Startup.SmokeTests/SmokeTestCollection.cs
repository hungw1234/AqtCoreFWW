using AqtCoreFW.Tests.SharedFixtures;

namespace AqtCoreFW.Startup.SmokeTests;

/// <summary>
/// xUnit collection for the SeederSmokeFixture-based test (ApiStartupSmokeTests).
/// Provides shared <see cref="SqlServerFixture"/> for tests that need it AND serializes
/// every test class tagged with <c>[Collection(CollectionName)]</c> within this assembly.
///
/// Connection strings no longer travel through a process-global env var — each fixture injects
/// its own via <c>builder.UseSetting</c> (see <c>UseTestConnectionStrings</c>), so there is no
/// shared mutable slot to race. The collection's remaining purpose is (1) sharing the
/// <see cref="SqlServerFixture"/> container and (2) capping the number of concurrent
/// Testcontainer SQL instances on a CI runner. Env-var serialization is NOT why it exists.
///
/// Tests using their own private Testcontainer (ApiHostFixture, AuthServerHostFixture)
/// opt into this collection for container-concurrency control, even though they don't consume
/// the shared SqlServerFixture.
///
/// Known limitation: SqlServerFixture uses the hard-coded DB name "AqtCoreFW_Test_Main"
/// on the local SQL path. Only relevant for tests that consume <see cref="SqlServerFixture"/>
/// (currently only ApiStartupSmokeTests via SeederSmokeFixture).
/// </summary>
[CollectionDefinition(CollectionName)]
public sealed class SmokeTestCollection : ICollectionFixture<SqlServerFixture>
{
    public const string CollectionName = "StartupSmoke";
}
