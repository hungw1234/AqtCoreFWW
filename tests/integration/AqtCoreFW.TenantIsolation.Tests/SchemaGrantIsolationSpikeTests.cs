using AqtCoreFW.TenantIsolation.Tests.Fixtures;
using Microsoft.Data.SqlClient;

namespace AqtCoreFW.TenantIsolation.Tests;

/// <summary>
/// C3 schema-GRANT isolation spike (red-team finding C3; plan 260713-0935-c3-schema-grant-isolation-spike).
///
/// Question: does a schema-scoped GRANT on the CURRENT shared monolith DB — all 14 module schemas in ONE database,
/// which is exactly the decision-#2 "logical DBs on a shared instance" topology — deliver the auditor-demonstrable
/// read-isolation ADR-012 claims requires extraction ("service A cannot read service B's data")? If yes, extracting
/// S4 to a separate logical DB buys nothing over a monolith login-config change for the read-isolation requirement.
///
/// Experiment: <c>files_svc_app</c> is a non-db_owner login granted ONLY <c>SELECT ON SCHEMA::[files]</c>.
///   (a) it CAN read [files]            → the schema grant works;
///   (b) it is DENIED [audit]           → default-deny isolates cross-schema (the proof — SqlException 229);
///   (c) it is NOT a member of db_owner → the result is not a false-green (IS_MEMBER('db_owner') = 0).
///
/// This is the empirical version of the test ADR-003:88 designed but never shipped ("users with only Identity schema
/// access cannot query Cases tables"). Verdict feeds the C3 gate — see the plan's Decision Mapping.
/// </summary>
[Collection(RlsPolicyPresenceCollection.CollectionName)]
public sealed class SchemaGrantIsolationSpikeTests
{
    // SQL Server error: "The SELECT permission was denied on the object ..."
    private const int SelectPermissionDeniedError = 229;

    private readonly RlsPolicyPresenceFixture _fixture;

    public SchemaGrantIsolationSpikeTests(RlsPolicyPresenceFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task FilesSvcApp_CanRead_ItsOwnFilesSchema()
    {
        await using var conn = new SqlConnection(_fixture.FilesSvcConnectionString);
        await conn.OpenAsync();

        // Positive leg: a granted schema is readable. Proves the login/connection works, so the negative leg's
        // failure below is a *permission* result, not a connection failure (A/B non-vacuous). An empty table is
        // fine — ExecuteScalar returns null with no exception; we only assert the SELECT is permitted.
        await using var cmd = new SqlCommand("SELECT TOP 1 1 FROM [files].[FileRecords];", conn);
        var ex = await Record.ExceptionAsync(() => cmd.ExecuteScalarAsync());

        Assert.Null(ex);
    }

    [Fact]
    public async Task FilesSvcApp_IsDenied_CrossSchemaAuditRead()
    {
        await using var conn = new SqlConnection(_fixture.FilesSvcConnectionString);
        await conn.OpenAsync();

        // The proof: a login granted only on [files] cannot read [audit] on the SAME shared database.
        await using var cmd = new SqlCommand("SELECT TOP 1 1 FROM [audit].[AuditLogEntries];", conn);
        var ex = await Assert.ThrowsAsync<SqlException>(() => cmd.ExecuteScalarAsync());

        // Assert on the error number, not the (localizable) message text.
        Assert.Equal(SelectPermissionDeniedError, ex.Number);
    }

    [Fact]
    public async Task FilesSvcApp_IsNotDbOwner_SoTheProofIsNonVacuous()
    {
        await using var conn = new SqlConnection(_fixture.FilesSvcConnectionString);
        await conn.OpenAsync();

        // Non-vacuous guard against the exact false-green class this spike investigates (red-team H4/SA5): a
        // db_owner login has IS_MEMBER('db_owner') = 1, which short-circuits every permission check — the
        // cross-schema denial above would then be impossible and the isolation "proof" would be meaningless.
        await using var cmd = new SqlCommand("SELECT IS_MEMBER('db_owner');", conn);
        var result = await cmd.ExecuteScalarAsync();

        Assert.Equal(0, Convert.ToInt32(result));
    }
}
