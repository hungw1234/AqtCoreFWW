using AqtCoreFW.Audit.Infrastructure.Persistence;
using AqtCoreFW.Files.Infrastructure.Persistence;
using AqtCoreFW.TenantIsolation.Tests.Fixtures;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.TenantIsolation.Tests;

/// <summary>
/// EF-path proof for the audit read-isolation boundary (ISO-EF-FIRST-AUDIT-BOUNDARY, plan 260713-1720).
///
/// The C3 spike (<see cref="SchemaGrantIsolationSpikeTests"/>) proved the boundary through RAW ADO
/// (a hand-written <see cref="SqlCommand"/>). This slice isolates the *co-dominant read path* — Entity
/// Framework — so it needs the EF analog: a <see cref="DbContext"/> whose registration connection string is
/// the non-audit service login must be DENIED <c>[audit]</c> through EF LINQ, while it can still read its
/// own schema. Same DB permission fact, different client sink — the sink Phase 2 rewires and Phase 3 guards.
///
/// Login = <c>files_svc_app</c> (fixture): non-db_owner, <c>GRANT SELECT ON SCHEMA::[files]</c> ONLY,
/// default-deny everything else — the reusable model of a per-service least-privilege login.
///
/// Non-vacuity (feedback-sa-dbowner-false-greens-rls-assertions): the positive leg proves the login is not
/// blanket-denied, and <c>IS_MEMBER('db_owner') = 0</c> proves the denial is real GRANT enforcement, not a
/// db_owner short-circuit that would false-green every isolation assertion.
/// </summary>
[Collection(RlsPolicyPresenceCollection.CollectionName)]
public sealed class AuditBoundaryEfIsolationTests
{
    // SQL Server: "The SELECT permission was denied on the object ...".
    private const int SelectPermissionDeniedError = 229;

    private readonly RlsPolicyPresenceFixture _fixture;

    public AuditBoundaryEfIsolationTests(RlsPolicyPresenceFixture fixture) => _fixture = fixture;

    // ── Positive leg: EF read of the granted schema is permitted ──────────────
    // Establishes that the negative leg's failure is a *permission* result, not a broken
    // context/connection (A/B non-vacuous). RLS may filter to 0 rows with no TenantId context —
    // an empty result is still a successful read, so we assert only that no exception is thrown.
    [Fact]
    public async Task NonAuditServiceLogin_ReadsOwnSchemaViaEf_Permitted()
    {
        await using var files = NewFilesContext(_fixture.FilesSvcConnectionString);

        var ex = await Record.ExceptionAsync(
            () => files.FileRecords.AsNoTracking().Take(1).ToListAsync());

        Assert.Null(ex);
    }

    // ── Negative leg: the SAME login cannot reach [audit] through EF ───────────
    // The proof — a non-audit service login is denied the audit schema even via the EF read path,
    // not only via raw ADO. EF propagates the raw SqlException on reads (it wraps only on SaveChanges),
    // so the permission check surfaces as SqlException 229 before any row is materialized.
    [Fact]
    public async Task NonAuditServiceLogin_ReadsAuditViaEf_DeniedWith229()
    {
        await using var audit = NewAuditContext(_fixture.FilesSvcConnectionString);

        var ex = await Assert.ThrowsAsync<SqlException>(
            () => audit.AuditLogEntries.AsNoTracking().Take(1).ToListAsync());

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

    // ── Context factories ─────────────────────────────────────────────────────
    // Each context is registered directly against the constrained login's connection string — the
    // production analog of pointing a non-audit module's DbContext at the non-audit login (Phase 2).

    private static AuditDbContext NewAuditContext(string connectionString)
        => new(new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlServer(connectionString)
            .EnableServiceProviderCaching(false)
            .Options);

    private static FilesDbContext NewFilesContext(string connectionString)
        => new(
            new DbContextOptionsBuilder<FilesDbContext>()
                .UseSqlServer(connectionString)
                .EnableServiceProviderCaching(false)
                .Options,
            new TestTenantContext(null, isSystemAdmin: false),
            keyResolver: null);
}
