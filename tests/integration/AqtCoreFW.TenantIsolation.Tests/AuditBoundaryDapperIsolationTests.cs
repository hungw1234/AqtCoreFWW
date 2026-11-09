using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.TenantIsolation.Tests.Fixtures;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AqtCoreFW.TenantIsolation.Tests;

/// <summary>
/// Dapper-path proof for the audit read-isolation boundary (ISO-DAPPER-KEYED-DI-INCREMENT, Phase 3a).
///
/// <see cref="AuditBoundaryEfIsolationTests"/> proved the boundary through EF. This slice proves the
/// SAME login denial through the OTHER co-dominant read sink — <see cref="DapperReadDbConnection"/> —
/// which is what every query handler actually uses (EF is repository/write-side; Dapper is read-side).
///
/// Login = <c>files_svc_app</c> (fixture): non-db_owner, <c>GRANT SELECT ON SCHEMA::[files]</c> ONLY,
/// default-deny everything else — the reusable NonAudit-login analog (Phase 2 wires the standalone
/// NonAudit login the same way in production; this fixture login proves the DB-permission fact once,
/// independent of which named login carries it).
///
/// The reader is hand-constructed with the constrained login as the 4th ctor arg
/// (<c>connectionStringOverride</c>) — the same mechanism Phase 2's DI registrations use to select a
/// login per-registration (see <c>InfrastructureRegistration.AddInfrastructure</c>).
/// <c>IsSystemAdmin: true</c> on the <see cref="TestTenantContext"/> is deliberate: it proves the DENY is
/// a DB-permission fact, not a handler-level <c>IsSystemAdmin</c> gate — a system-admin caller through
/// this login is STILL refused by SQL Server itself.
///
/// F1/F2 proof (review findings): both SESSION_CONTEXT bypass openers
/// (<see cref="DapperReadDbConnection.QueryWithAuditAdminBypassAsync{T}"/> and
/// <see cref="DapperReadDbConnection.QueryFirstOrDefaultWithPublicFormsBypassAsync{T}"/>) build their
/// <see cref="SqlConnection"/> off the SAME <c>ConnectionString</c> property as the standard opener — the
/// bypass flag changes RLS visibility, it does NOT change which login opens the socket. A NonAudit-bound
/// reader is therefore denied <c>[audit]</c> even through the bypass variants; the SESSION_CONTEXT flags
/// they set are meaningless once the SELECT permission check itself fails first.
/// </summary>
[Collection(RlsPolicyPresenceCollection.CollectionName)]
public sealed class AuditBoundaryDapperIsolationTests
{
    // SQL Server: "The SELECT permission was denied on the object ...".
    private const int SelectPermissionDeniedError = 229;

    private readonly RlsPolicyPresenceFixture _fixture;

    public AuditBoundaryDapperIsolationTests(RlsPolicyPresenceFixture fixture) => _fixture = fixture;

    // ── Positive leg: Dapper read of the granted schema is permitted ──────────
    // Establishes that the negative legs' failures are a *permission* result, not a broken
    // connection string / login (A/B non-vacuous). RLS may filter to 0 rows with no TenantId
    // context — an empty result is still a successful read, so we assert only that no exception
    // is thrown.
    [Fact]
    public async Task NonAuditServiceLogin_ReadsOwnSchemaViaDapper_Permitted()
    {
        var db = NewReader(_fixture.FilesSvcConnectionString);

        var ex = await Record.ExceptionAsync(
            () => db.QueryAsync<int>("SELECT TOP 1 1 FROM [files].FileRecords"));

        Assert.Null(ex);
    }

    // ── Negative leg: the standard opener cannot reach [audit] ────────────────
    [Fact]
    public async Task NonAuditServiceLogin_ReadsAuditViaDapper_DeniedWith229()
    {
        var db = NewReader(_fixture.FilesSvcConnectionString);

        var ex = await Assert.ThrowsAsync<SqlException>(
            () => db.QueryAsync<int>("SELECT TOP 1 1 FROM [audit].AuditLogEntries"));

        Assert.Equal(SelectPermissionDeniedError, ex.Number);
    }

    // ── Negative leg (F1 proof): the audit-admin-bypass opener ALSO cannot reach [audit] ─
    // IsSystemAdmin: true on the TestTenantContext (see NewReader) means the handler-level gate
    // would happily let this call through — the DB permission on this login is the ONLY thing
    // denying it. If this ever returns rows instead of throwing 229, the bypass path has silently
    // started opening its connection on a broader login than the standard opener — a real
    // isolation hole, not a test artifact.
    [Fact]
    public async Task NonAuditServiceLogin_ReadsAuditViaDapperAuditAdminBypass_DeniedWith229()
    {
        var db = NewReader(_fixture.FilesSvcConnectionString);

        var ex = await Assert.ThrowsAsync<SqlException>(
            () => db.QueryWithAuditAdminBypassAsync<int>("SELECT TOP 1 1 FROM [audit].AuditLogEntries"));

        Assert.Equal(SelectPermissionDeniedError, ex.Number);
    }

    // ── Negative leg (F2 proof): the public-forms-bypass opener ALSO cannot reach [audit] ─
    // Same proof as above for the OTHER bypass opener — public_forms_bypass targets FormTemplates
    // visibility, but it opens its connection through the identical ConnectionString property, so
    // it inherits the same NonAudit-login denial.
    [Fact]
    public async Task NonAuditServiceLogin_ReadsAuditViaDapperPublicFormsBypass_DeniedWith229()
    {
        var db = NewReader(_fixture.FilesSvcConnectionString);

        var ex = await Assert.ThrowsAsync<SqlException>(
            () => db.QueryFirstOrDefaultWithPublicFormsBypassAsync<int>(
                "SELECT TOP 1 1 FROM [audit].AuditLogEntries"));

        Assert.Equal(SelectPermissionDeniedError, ex.Number);
    }

    // ── Non-vacuity guard: the login is a genuine least-privilege principal ────
    [Fact]
    public async Task NonAuditServiceLogin_IsNotDbOwner_SoTheProofIsNonVacuous()
    {
        await using var conn = new SqlConnection(_fixture.FilesSvcConnectionString);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand("SELECT IS_MEMBER('db_owner');", conn);
        var result = await cmd.ExecuteScalarAsync();

        Assert.Equal(0, Convert.ToInt32(result));
    }

    // ── Reader factory ──────────────────────────────────────────────────────────
    // Hand-constructs DapperReadDbConnection the way PublicFormsLeastPrivilegeTests.NewDapperRead does,
    // but pins connectionStringOverride to the constrained login (the ctor's 4th arg — the exact
    // mechanism InfrastructureRegistration uses to select a login per-registration) and sets
    // IsSystemAdmin: true so a handler-level admin gate could never explain a denial here — only the
    // DB GRANT can.
    private static DapperReadDbConnection NewReader(string connectionString)
    {
        var monitor = Substitute.For<IOptionsMonitor<DatabaseOptions>>();
        monitor.CurrentValue.Returns(new DatabaseOptions { ConnectionString = connectionString });
        var uow = Substitute.For<ICrossModuleUnitOfWork>();
        uow.IsActive.Returns(false);
        return new DapperReadDbConnection(
            monitor, new TestTenantContext(null, isSystemAdmin: true), uow, connectionString);
    }
}
