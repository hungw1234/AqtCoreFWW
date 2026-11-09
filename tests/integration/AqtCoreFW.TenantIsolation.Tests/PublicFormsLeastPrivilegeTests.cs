using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Application.Queries.GetPublicFormFieldOptions;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Infrastructure.Persistence;
using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.TenantIsolation.Tests.Fixtures;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.TenantIsolation.Tests;

/// <summary>
/// Verifies the FormTemplates public-read RLS carve-out (PUBLIC-FORMS-LEAST-PRIV) under a
/// non-db_owner login (<c>rls_test_app</c>) — the login that RLS predicates are actually
/// evaluated for (db_owner short-circuits <c>IS_MEMBER('db_owner')=1</c> and would false-green
/// every assertion here).
///
/// The anonymous public form paths resolve a template by Code on a connection with no ambient
/// tenant. Two mechanisms make that work under least-privilege:
///   (A) a <c>public_forms_bypass</c> Dapper read for the by-Code resolution (tenant unknown), and
///   (B) <see cref="FormTemplateRepository.GetPublicWithFieldsAsync"/> which pins SESSION_CONTEXT to
///       the resolved tenant for the EF second-step read (tenant now known).
///
/// Each test drives the REAL production type, and the negative tests prove the carve-out / pin is
/// load-bearing: without it the same read returns 0 rows (i.e. the fix, if removed, goes RED here).
/// </summary>
[Collection(RlsPolicyPresenceCollection.CollectionName)]
public sealed class PublicFormsLeastPrivilegeTests
{
    private readonly RlsPolicyPresenceFixture _fixture;

    public PublicFormsLeastPrivilegeTests(RlsPolicyPresenceFixture fixture) => _fixture = fixture;

    // Mirrors the handler by-Code resolution SQL (with the IsPublic=1 app-guard).
    private const string ByCodeSql = """
        SELECT Id, TenantId, PublicId FROM forms.FormTemplates
        WHERE Code = @Code AND Status = 'Active' AND IsDeleted = 0 AND IsPublic = 1
        """;

    // Same lookup WITHOUT the IsPublic app-guard, to isolate the RLS-layer behaviour from the
    // application WHERE clause (used by the negative RLS tests).
    private const string ByCodeSqlNoAppGuard = """
        SELECT Id, TenantId, PublicId FROM forms.FormTemplates
        WHERE Code = @Code AND Status = 'Active' AND IsDeleted = 0
        """;

    // ── TEST 1: bypass read admits an Active IsPublic=1 template under least-priv ─────────────
    [Fact]
    public async Task BypassRead_AdmitsPublicTemplate_UnderLeastPriv()
    {
        var tenantId = Guid.NewGuid();
        var (id, _, code) = await SeedActiveTemplateAsync(tenantId, isPublic: true);

        var db = NewDapperRead(_fixture.ConstrainedConnectionString, tenantId: null);
        var row = await db.QueryFirstOrDefaultWithPublicFormsBypassAsync<TemplateRow>(
            ByCodeSql, new { Code = code });

        row.Should().NotBeNull(
            "the public_forms_bypass carve-out must expose an Active IsPublic=1 template to an " +
            "anonymous, non-db_owner caller");
        row!.Id.Should().Be(id);
        row.TenantId.Should().Be(tenantId, "the server resolves the template's real tenant by Code");
    }

    // ── TEST 2: standard read (no bypass flag) is filtered — proves the carve-out is load-bearing ─
    [Fact]
    public async Task StandardRead_WithoutBypassFlag_ReturnsNull_UnderLeastPriv()
    {
        var tenantId = Guid.NewGuid();
        var (_, _, code) = await SeedActiveTemplateAsync(tenantId, isPublic: true);

        var db = NewDapperRead(_fixture.ConstrainedConnectionString, tenantId: null);
        var row = await db.QueryFirstOrDefaultAsync<TemplateRow>(ByCodeSql, new { Code = code });

        row.Should().BeNull(
            "without the public_forms_bypass flag the FormTemplates RLS FILTER hides the row from a " +
            "non-db_owner anonymous caller — if this returned a row the carve-out would be redundant " +
            "(false-green), so this asserts the fix is genuinely required");
    }

    // ── TEST 3: bypass flag does NOT expose a private (IsPublic=0) template ────────────────────
    [Fact]
    public async Task BypassRead_DoesNotExpose_NonPublicTemplate()
    {
        var tenantId = Guid.NewGuid();
        var (_, _, code) = await SeedActiveTemplateAsync(tenantId, isPublic: false);

        var db = NewDapperRead(_fixture.ConstrainedConnectionString, tenantId: null);
        // No app-guard: rely purely on the RLS carve-out, which requires IsPublic=1.
        var row = await db.QueryFirstOrDefaultWithPublicFormsBypassAsync<TemplateRow>(
            ByCodeSqlNoAppGuard, new { Code = code });

        row.Should().BeNull(
            "the RLS carve-out fires only for IsPublic=1 rows; a private template stays hidden even " +
            "with the bypass flag set");
    }

    // ── TEST 4: cross-tenant isolation — a tenant-pinned read cannot see another tenant's public template ─
    [Fact]
    public async Task TenantPinnedRead_CannotSee_OtherTenantsPublicTemplate()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var (_, _, codeA) = await SeedActiveTemplateAsync(tenantA, isPublic: true);

        // Pin SESSION_CONTEXT to tenant B (standard read, no bypass flag).
        var db = NewDapperRead(_fixture.ConstrainedConnectionString, tenantId: tenantB);
        var row = await db.QueryFirstOrDefaultAsync<TemplateRow>(ByCodeSqlNoAppGuard, new { Code = codeA });

        row.Should().BeNull(
            "public visibility is gated behind the server-set bypass flag, not opened cross-tenant — a " +
            "tenant-B-pinned authenticated read must never see tenant-A's public template");
    }

    // ── TEST 5: the bypass flag does not leak across pooled connections ───────────────────────
    [Fact]
    public async Task PublicFormsBypassFlag_DoesNotLeak_AfterRead()
    {
        var singleSlotCs = BuildSingleSlotConstrainedConnectionString();
        var tenantId = Guid.NewGuid();
        var (_, _, code) = await SeedActiveTemplateAsync(tenantId, isPublic: true);

        // Run a bypass read; the connection returns to the single-slot pool afterwards.
        var db = NewDapperRead(singleSlotCs, tenantId: null);
        await db.QueryFirstOrDefaultWithPublicFormsBypassAsync<TemplateRow>(ByCodeSql, new { Code = code });

        // Acquire the same physical connection and probe the flag.
        await using var probe = new SqlConnection(singleSlotCs);
        await probe.OpenAsync();
        var flag = await GetContextAsync(probe, "public_forms_bypass");

        flag.Should().BeNull(
            "public_forms_bypass must be cleared by sp_reset_connection before the connection is reused " +
            "(no @read_only=1), or a subsequent tenant caller would inherit the carve-out");
    }

    // ── TEST 6: GetPublicWithFieldsAsync pins the tenant for the EF read; standard read is filtered ─
    [Fact]
    public async Task GetPublicWithFieldsAsync_PinsTenant_UnderLeastPriv()
    {
        var tenantId = Guid.NewGuid();
        var (id, _, _) = await SeedActiveTemplateAsync(tenantId, isPublic: true);

        // Standard EF read: no pin → RLS FILTER hides the row under least-priv (proves the pin matters).
        await using (var ctxStd = NewFormsContext(_fixture.ConstrainedConnectionString))
        {
            var repoStd = new FormTemplateRepository(ctxStd);
            var viaStandard = await repoStd.GetWithFieldsAsync(id, tenantId);
            viaStandard.Should().BeNull(
                "the standard EF read leaves SESSION_CONTEXT unset for an anonymous caller → the " +
                "FormTemplates RLS FILTER returns 0 rows under a non-db_owner login");
        }

        // Public EF read: pins SESSION_CONTEXT to the resolved tenant → passes the standard predicate.
        await using (var ctxPin = NewFormsContext(_fixture.ConstrainedConnectionString))
        {
            var repoPin = new FormTemplateRepository(ctxPin);
            var viaPin = await repoPin.GetPublicWithFieldsAsync(id, tenantId);
            viaPin.Should().NotBeNull(
                "GetPublicWithFieldsAsync pins SESSION_CONTEXT to the server-resolved tenant, so the EF " +
                "template load passes the standard FormTemplates RLS FILTER under least-priv");
            viaPin!.Id.Should().Be(id);
        }
    }

    // ── TEST 7: the filtered unique index rejects a second IsPublic=1 row sharing a Code ──────────
    // Backstop for PUBLIC-FORMS-CODE-GLOBAL-UNIQUENESS: independent of the app-guard, the DB index
    // UX_FormTemplate_PublicCode must make a globally-duplicate public Code impossible. Enforced even
    // under sa (unique indexes apply to db_owner; RLS governs row visibility, not constraint checks).
    [Fact]
    public async Task PublicCodeUniqueIndex_RejectsSecondPublicRow_WithSameCode()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var (_, _, code) = await SeedActiveTemplateAsync(tenantA, isPublic: true);
        // Tenant B may share the Code (the per-tenant (TenantId, Code) index allows it) while private.
        var (idB, _, _) = await SeedActiveTemplateAsync(tenantB, isPublic: false, code: code);

        // Flipping B public would create a 2nd IsPublic=1 row for this Code → the filtered unique index
        // must reject it. Proves the DB backstop is load-bearing even if the app-guard is bypassed.
        var act = async () => await ExecuteSaAsync(
            "UPDATE [forms].[FormTemplates] SET IsPublic = 1 WHERE Id = @id", ("@id", idB));

        (await act.Should().ThrowAsync<SqlException>())
            .Which.Number.Should().BeOneOf(new[] { 2601, 2627 },
                "a duplicate key on UX_FormTemplate_PublicCode surfaces as SQL error 2601/2627");
    }

    // ── TEST 8: the admin public-Code guard sees a cross-tenant public row ONLY via the bypass ────
    // SetFormTemplatePublicAccessCommandHandler's guard must detect a collision in ANOTHER tenant.
    // Under least-priv the tenant-pinned admin read is RLS-filtered, so the guard MUST use the
    // public_forms_bypass flag. Driving the guard's EXACT probe SQL here proves that bypass is
    // load-bearing (swap it for a plain read → this goes RED), closing the false-green that a
    // db_owner-only API test would leave (the API test runs as sa, where IS_MEMBER('db_owner')=1
    // short-circuits the FILTER and would pass even for a broken plain-EF probe).
    [Fact]
    public async Task PublicCodeGuardProbe_SeesCrossTenantPublicCode_OnlyWithBypass()
    {
        var tenantA = Guid.NewGuid();
        var (_, _, code) = await SeedActiveTemplateAsync(tenantA, isPublic: true);

        // Mirrors the handler guard's probe SQL exactly; ExcludeId sentinel excludes no real row.
        const string guardProbeSql = """
            SELECT TOP 1 1 FROM forms.FormTemplates
            WHERE Code = @Code AND IsPublic = 1 AND IsDeleted = 0 AND Id <> @ExcludeId
            """;

        // With bypass (what the guard actually does): sees tenant-A's public row cross-tenant.
        var dbBypass = NewDapperRead(_fixture.ConstrainedConnectionString, tenantId: null);
        var hitWithBypass = await dbBypass.QueryFirstOrDefaultWithPublicFormsBypassAsync<int?>(
            guardProbeSql, new { Code = code, ExcludeId = -1L });
        hitWithBypass.Should().NotBeNull(
            "the guard's public_forms_bypass probe must see another tenant's IsPublic=1 row so enabling a " +
            "colliding public Code is rejected (409) even under a non-db_owner login");

        // Without bypass (a hypothetical plain-read regression): the RLS FILTER hides the cross-tenant
        // row, so the guard would fall open in prod — proving the bypass is required, not incidental.
        var dbStandard = NewDapperRead(_fixture.ConstrainedConnectionString, tenantId: null);
        var hitWithoutBypass = await dbStandard.QueryFirstOrDefaultAsync<int?>(
            guardProbeSql, new { Code = code, ExcludeId = -1L });
        hitWithoutBypass.Should().BeNull(
            "without the bypass flag the FormTemplates RLS FILTER hides the cross-tenant public row from a " +
            "non-db_owner caller — if this returned a row the bypass would be redundant (false-green)");
    }

    // ── TEST 9: renaming a public template onto another tenant's public Code trips the unique index ──
    // Backstop for PUBLIC-FORMS-SETCODE-PUBLIC-RENAME-409: the SetCode path (UpdateFormTemplateCode)
    // renames a Draft template via an UPDATE of the Code column. Test 7 covers the IsPublic-flip UPDATE;
    // this covers the *Code* UPDATE — a public template renamed to a Code already public in ANOTHER
    // tenant must be rejected by UX_FormTemplate_PublicCode. That raw SQL 2601/2627 is exactly the 500
    // the handler's 409 guard turns friendly; the index stays the load-bearing backstop even if the
    // guard is bypassed (unique indexes apply to db_owner; RLS governs visibility, not constraints).
    [Fact]
    public async Task PublicCodeUniqueIndex_RejectsPublicRename_ToCollidingCode()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var (_, _, codeA) = await SeedActiveTemplateAsync(tenantA, isPublic: true);
        // Tenant B owns a distinct public Code; renaming it onto tenant-A's public Code must collide.
        var (idB, _, _) = await SeedActiveTemplateAsync(tenantB, isPublic: true);

        var act = async () => await ExecuteSaAsync(
            "UPDATE [forms].[FormTemplates] SET Code = @code WHERE Id = @id", ("@code", codeA), ("@id", idB));

        (await act.Should().ThrowAsync<SqlException>())
            .Which.Number.Should().BeOneOf(new[] { 2601, 2627 },
                "renaming a public template onto another tenant's public Code violates " +
                "UX_FormTemplate_PublicCode (SQL 2601/2627) — the raw error the SetCode 409 guard prevents");
    }

    // ── TEST 10: C1 — the public field-options handler resolves an ACTIVE field of a public form ──
    // under least-priv, end-to-end (Dapper bypass by-Code → GetPublicWithFieldsAsync pin → pick by key).
    [Fact]
    public async Task PublicFieldOptionsHandler_ResolvesActiveField_UnderLeastPriv()
    {
        var tenantId = Guid.NewGuid();
        var (_, _, code) = await SeedActiveTemplateWithFieldAsync(
            tenantId, fieldKey: "color", isActive: true, withStaticOption: true);

        var result = await RunPublicFieldOptionsHandlerAsync(code, "color");

        result.IsSuccess.Should().BeTrue(
            "an active field of an IsPublic=1 template must resolve for an anonymous caller under a " +
            "non-db_owner login — the by-Code bypass + GetPublicWithFieldsAsync pin carry the read");
        result.Value.Should().ContainSingle(o => o.Value == "opt1");
    }

    // ── TEST 11: C1 — a DEACTIVATED field's key is not resolvable on the anonymous surface ────────
    // Mirrors the handler's IsActive filter (code-review H1). Even though the row is loaded by
    // GetPublicWithFieldsAsync (Include has no active filter), the handler must not resolve it — a
    // retired field must be off the public surface, proven end-to-end under least-priv.
    [Fact]
    public async Task PublicFieldOptionsHandler_DoesNotResolveDeactivatedField_UnderLeastPriv()
    {
        var tenantId = Guid.NewGuid();
        var (_, _, code) = await SeedActiveTemplateWithFieldAsync(
            tenantId, fieldKey: "retired", isActive: false, withStaticOption: true);

        var result = await RunPublicFieldOptionsHandlerAsync(code, "retired");

        result.IsFailed.Should().BeTrue(
            "a deactivated field must not be addressable on the anonymous surface (IsActive filter) — " +
            "if this resolved, a retired field's options (and, for ExternalRef, its outbound fetch) " +
            "would be reachable by anyone who knows the key");
    }

    // Drives the REAL GetPublicFormFieldOptionsQueryHandler against the constrained (non-db_owner)
    // connection, exactly as the anonymous request path does (no ambient tenant, server-resolved).
    private async Task<FluentResults.Result<IReadOnlyList<DataSourceOptionDto>>> RunPublicFieldOptionsHandlerAsync(
        string code, string fieldKey)
    {
        var db = NewDapperRead(_fixture.ConstrainedConnectionString, tenantId: null);
        await using var ctx = NewFormsContext(_fixture.ConstrainedConnectionString);
        var templateRepo = new FormTemplateRepository(ctx);
        var factory = Substitute.For<IDataSourceResolverFactory>();
        var handler = new GetPublicFormFieldOptionsQueryHandler(db, templateRepo, factory);
        return await handler.Handle(new GetPublicFormFieldOptionsQuery(code, fieldKey), CancellationToken.None);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────

    private sealed class TemplateRow
    {
        public long Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid PublicId { get; set; }
    }

    /// <summary>
    /// Seeds an Active template via <c>sa</c> (db_owner bypasses the RLS BLOCK predicate). The row is
    /// EF-inserted as a Draft (handles the wide audit/rowversion column set) then flipped to
    /// Active + the requested IsPublic value with a direct UPDATE — the domain <c>Publish()</c> path
    /// requires an active field, which is irrelevant to the RLS behaviour under test.
    /// </summary>
    private async Task<(long Id, Guid PublicId, string Code)> SeedActiveTemplateAsync(
        Guid tenantId, bool isPublic, string? code = null)
    {
        code ??= "plp-" + Guid.NewGuid().ToString("N")[..12];
        long id;
        Guid publicId;

        await using (var ctx = NewFormsContext(_fixture.ConnectionString))
        {
            var tpl = FormTemplate.Create(
                GuidGen, tenantId, "PLP Least-Priv", "PLP Least-Priv VI", code,
                StorageMode.Json, Guid.NewGuid());
            ctx.FormTemplates.Add(tpl);
            await ctx.SaveChangesAsync();
            id = tpl.Id;
            publicId = tpl.PublicId;
        }

        await ExecuteSaAsync(
            "UPDATE [forms].[FormTemplates] SET Status = 'Active', IsPublic = @isPublic WHERE Id = @id",
            ("@isPublic", isPublic), ("@id", id));

        return (id, publicId, code);
    }

    /// <summary>
    /// Seeds an Active, IsPublic=1 template carrying one field (EF-inserted as Draft with the field +
    /// an optional static option, then flipped Active+public via sa). When <paramref name="isActive"/>
    /// is false the field is deactivated (soft flag) so the anonymous-surface IsActive filter can be
    /// exercised end-to-end.
    /// </summary>
    private async Task<(long Id, Guid PublicId, string Code)> SeedActiveTemplateWithFieldAsync(
        Guid tenantId, string fieldKey, bool isActive, bool withStaticOption)
    {
        var code = "plp-" + Guid.NewGuid().ToString("N")[..12];
        long id;
        Guid publicId;

        await using (var ctx = NewFormsContext(_fixture.ConnectionString))
        {
            var tpl = FormTemplate.Create(
                GuidGen, tenantId, "PLP Field", "PLP Field VI", code, StorageMode.Json, Guid.NewGuid());
            var field = tpl.AddField(
                fieldKey: fieldKey, type: FormFieldType.EnumRef, labelVi: "Nhãn", labelEn: "Label",
                displayOrder: 1, required: false, validationRulesJson: null, updatedBy: Guid.NewGuid());
            if (withStaticOption)
                field.AddOption(FormFieldOption.Create(field.Id, "opt1", "Tùy chọn 1", "Option 1", 1));
            if (!isActive)
                field.Deactivate();
            ctx.FormTemplates.Add(tpl);
            await ctx.SaveChangesAsync();
            id = tpl.Id;
            publicId = tpl.PublicId;
        }

        await ExecuteSaAsync(
            "UPDATE [forms].[FormTemplates] SET Status = 'Active', IsPublic = 1 WHERE Id = @id",
            ("@id", id));

        return (id, publicId, code);
    }

    private async Task ExecuteSaAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var conn = new SqlConnection(_fixture.ConnectionString); // sa = db_owner
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }

    private static FormsDbContext NewFormsContext(string connectionString)
    {
        var opts = new DbContextOptionsBuilder<FormsDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "forms"))
            .EnableServiceProviderCaching(false)
            .Options;
        // Anonymous, non-admin tenant context: mirrors the public path (no ambient tenant), so the EF
        // global filter passes (null-tenant → true) and only the DB-level RLS FILTER governs the read.
        return new FormsDbContext(opts, new TestTenantContext(tenantId: null, isSystemAdmin: false));
    }

    private static DapperReadDbConnection NewDapperRead(string connectionString, Guid? tenantId)
    {
        var monitor = Substitute.For<IOptionsMonitor<DatabaseOptions>>();
        monitor.CurrentValue.Returns(new DatabaseOptions { ConnectionString = connectionString });
        var uow = Substitute.For<ICrossModuleUnitOfWork>();
        uow.IsActive.Returns(false);
        return new DapperReadDbConnection(monitor, new TestTenantContext(tenantId, isSystemAdmin: false), uow);
    }

    private string BuildSingleSlotConstrainedConnectionString()
    {
        var csb = new SqlConnectionStringBuilder(_fixture.ConstrainedConnectionString)
        {
            MaxPoolSize = 1,
            MinPoolSize = 1,
            Pooling = true,
        };
        return csb.ConnectionString;
    }

    private static async Task<string?> GetContextAsync(SqlConnection conn, string key)
    {
        await using var cmd = new SqlCommand(
            $"SELECT CAST(SESSION_CONTEXT(N'{key}') AS nvarchar(500))", conn);
        var result = await cmd.ExecuteScalarAsync();
        return result == DBNull.Value ? null : result as string;
    }

    private static readonly IGuidGenerator GuidGen = new SeqGuidGenerator();

    private sealed class SeqGuidGenerator : IGuidGenerator
    {
        public Guid NewIdMonotonic() => Guid.NewGuid();
        public Guid NewIdRandom() => Guid.NewGuid();
    }
}
