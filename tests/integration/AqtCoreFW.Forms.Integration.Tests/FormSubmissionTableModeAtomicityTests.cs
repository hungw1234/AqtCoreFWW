using AqtCoreFW.Api;
using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.Forms.Infrastructure.Persistence;
using AqtCoreFW.Identity.Domain.Models;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AqtCoreFW.Forms.Integration.Tests;

/// <summary>
/// Sóng-0 Phase 2, Success Criterion #2 — proves the C2 UoW-atomicity fix (span-atomic
/// AddAsync + storage.WriteAsync + per-TableField InsertRowsAsync + outbox enqueue in
/// <see cref="AqtCoreFW.Forms.Application.Commands.SubmitForm.SubmitFormCommandHandler"/>)
/// holds for the Table-mode CHILD-ROW path specifically. <see cref="FormSubmissionOutboxAtomicityTests"/>
/// already proves the same span for the Json-mode parent-write path (via a simulated
/// <c>IFormDataStorage.WriteAsync</c> throw); this file drives a REAL failure of the REAL
/// <see cref="ITableFieldDataRepository.InsertRowsAsync"/> (a genuine SqlException from a
/// missing child table) — belt-and-suspenders on the exact code path Phase-3 TableField support
/// added.
///
/// Also serves as the regression guard for three real, independent Table-mode defects found and
/// since fixed while building this harness (all in <c>src/</c>, not this file):
/// <list type="bullet">
///   <item><description><b>Defect A</b> — <c>AddFormFieldRequest</c>/<c>FormTemplatesController</c>
///         did not expose/forward <c>ParentFieldId</c>, so a TableField's child field could not be
///         created via the real HTTP API. Fixed: <c>AddFormFieldRequest.ParentFieldId</c> now
///         forwards through. This file now creates the child field via the real HTTP endpoint.</description></item>
///   <item><description><b>Defect B</b> — <c>FormTableMigrator.CreateOrReplaceViewAsync</c> /
///         <c>CreateOrReplaceChildViewAsync</c> joined on <c>FormSubmission.Id</c> (bigint) instead
///         of <c>PublicId</c> (uniqueidentifier), throwing an operand-type-clash SqlException on
///         every Table-mode materialization. Fixed: both now join on <c>s.PublicId</c>. This file's
///         harness now calls both view-creation steps and asserts the views are queryable.</description></item>
///   <item><description><b>Defect C</b> — <c>FormDataPreprocessor.PreprocessData</c>'s default case
///         passed raw <c>JsonElement</c> scalar values through to <c>TableFormDataStorage.WriteAsync</c>,
///         which Dapper rejects. Fixed: scalar JsonElement values are normalized to native CLR before
///         reaching Dapper (Array/Object shapes still pass through untouched for
///         <c>ParseTableFieldRows</c>). This file's happy-path test now submits a real non-null
///         scalar value and asserts it lands in the parent dynamic table.</description></item>
///   <item><description><b>PUBLIC-FORMS-TABLEFIELD-CHILDROW-DROP</b> — <c>SubmitPublicFormCommandHandler</c>
///         (the anonymous public submit path) persisted the Table-mode parent row but never called
///         <c>ITableFieldDataRepository</c>, silently dropping every TableField child row. Fixed: the
///         handler now writes child rows inside the same atomic span, mirroring the authenticated
///         path. Guarded by this file's public-path test.</description></item>
/// </list>
///
/// Uses the REAL SQL Server integration DB (<see cref="WebAppFixture"/>) — NOT InMemory/mocked
/// EF, which would strip the real transaction semantics this atomicity proof depends on.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class FormSubmissionTableModeAtomicityTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid AdminId  = Guid.NewGuid();
    private static readonly Guid UserId   = Guid.NewGuid();

    // Mirrors FormMaterializationService.SystemUserId — audit actor for the synchronous
    // Materializing->Active flip this harness performs in place of the (stripped) background service.
    private static readonly Guid SystemUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public FormSubmissionTableModeAtomicityTests(WebAppFixture app) => _app = app;

    // --- Happy path: real child table provisioned, real InsertRowsAsync succeeds ---

    [Fact]
    public async Task Submit_TableMode_ChildTableProvisioned_InsertsRowAndEnqueuesExactlyOneOutboxRow()
    {
        var admin = CreateClientWithPermissions(_app, AdminId, TenantId, PermCodes.Forms.Template.Manage);
        var user  = CreateClientWithPermissions(_app, UserId, TenantId,
            PermCodes.Forms.Submit.Create, PermCodes.Forms.Submit.View);
        var code  = $"TBH{Guid.NewGuid():N}"[..9];

        var (templatePublicId, tableFieldId) = await CreateTableModeTemplateAsync(admin, code);
        var templateId = await MaterializeTableTemplateAsync(_app, templatePublicId, includeChildTables: true);

        // Defect C regression guard: "fullName" now carries a real non-null scalar value — before
        // the fix this would throw NotSupportedException in TableFormDataStorage.WriteAsync's Dapper
        // call before ever reaching the child-row insert this test targets.
        var submitResp = await user.PostAsJsonAsync("/api/v1/forms/submissions", new
        {
            formTemplatePublicId = templatePublicId,
            data = new Dictionary<string, object?>
            {
                ["fullName"] = "Nguyen Van A",
                ["items"] = new object[]
                {
                    new Dictionary<string, object?> { ["itemName"] = "Widget" }
                }
            }
        });

        submitResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var submissionId = (await submitResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetGuid();

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();

        var outboxRows = await db.OutboxMessages
            .Where(m => m.Payload.Contains(submissionId.ToString()))
            .ToListAsync();
        outboxRows.Should().HaveCount(1,
            "Table-mode submit must enqueue exactly one outbox row, same as the Json-mode path");

        // Defect C regression guard: the scalar parent write must have landed the real (normalized)
        // CLR value in the dynamic parent table, not thrown or silently dropped it.
        var storageFactory = scope.ServiceProvider.GetRequiredService<IFormDataStorageFactory>();
        var parentStorage = storageFactory.Create(StorageMode.Table);
        var parentRow = await parentStorage.ReadAsync(templateId, submissionId);
        parentRow.Should().NotBeNull();
        parentRow!["fullName"].Should().Be("Nguyen Van A",
            "the normalized scalar value must persist in the parent dynamic table, not a raw JsonElement");

        // Real InsertRowsAsync against the real child table — queryable via the real repository.
        var tableFieldDataRepo = scope.ServiceProvider.GetRequiredService<ITableFieldDataRepository>();
        var rows = await tableFieldDataRepo.GetRowsAsync(templateId, tableFieldId, submissionId);
        rows.Should().HaveCount(1,
            "the real InsertRowsAsync call inside the atomic write span must have persisted the child row");
    }

    // --- Atomicity: a real InsertRowsAsync failure (missing child table) rolls back BOTH ---

    [Fact]
    public async Task Submit_TableMode_ChildTableMissing_RealInsertRowsFailure_RollsBackSubmissionAndOutboxRow()
    {
        var admin = CreateClientWithPermissions(_app, AdminId, TenantId, PermCodes.Forms.Template.Manage);
        var user  = CreateClientWithPermissions(_app, UserId, TenantId,
            PermCodes.Forms.Submit.Create, PermCodes.Forms.Submit.View);
        var code  = $"TBR{Guid.NewGuid():N}"[..9];

        var (templatePublicId, _) = await CreateTableModeTemplateAsync(admin, code);

        // Parent table/view ARE materialized (storage.WriteAsync succeeds) but the child table is
        // deliberately SKIPPED — this is the real-failure seam: ITableFieldDataRepository.InsertRowsAsync
        // issues a real INSERT against forms.[SubmTableRows_{templateId}_{tableFieldId}], which does
        // not exist, producing a genuine SqlException (Invalid object name) inside the write span.
        var templateId = await MaterializeTableTemplateAsync(_app, templatePublicId, includeChildTables: false);

        var submitResp = await user.PostAsJsonAsync("/api/v1/forms/submissions", new
        {
            formTemplatePublicId = templatePublicId,
            data = new Dictionary<string, object?>
            {
                ["fullName"] = "Should Not Persist",
                ["items"] = new object[]
                {
                    new Dictionary<string, object?> { ["itemName"] = "Orphan Row" }
                }
            }
        });

        submitResp.StatusCode.Should().Be(HttpStatusCode.InternalServerError,
            "the real child-table INSERT throws an unhandled SqlException — GlobalExceptionHandlerMiddleware " +
            "maps it to 500, same as the Json-mode simulated-failure test; the write span must roll back " +
            "atomically instead of leaving a partially-committed submission");

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();

        var submissionCount = await db.FormSubmissions
            .IgnoreQueryFilters()
            .CountAsync(s => s.FormTemplateId == templateId);
        submissionCount.Should().Be(0,
            "a rolled-back Table-mode submit must not leave an orphan FormSubmission row — the same C2 " +
            "invariant proven for Json mode, now proven for the real per-TableField child-row write");

        var outboxCount = await db.OutboxMessages
            .CountAsync(m => m.Payload.Contains(templatePublicId.ToString()));
        outboxCount.Should().Be(0,
            "no outbox event may survive a submission whose child-row write never actually committed");
    }

    // --- Public path: anonymous Table-mode submit must not silently drop child rows ---

    /// <summary>
    /// Regression guard for PUBLIC-FORMS-TABLEFIELD-CHILDROW-DROP: <c>SubmitPublicFormCommandHandler</c>
    /// previously persisted the Table-mode parent row but never called <c>ITableFieldDataRepository</c>,
    /// so every TableField child row on an anonymous public submit was silently dropped. Fixed: the
    /// handler now injects <c>ITableFieldDataRepository</c> and writes child rows inside the SAME
    /// atomic write span as the authenticated <c>SubmitFormCommandHandler</c> path.
    ///
    /// Uses a bare anonymous <c>HttpClient</c> (no auth headers → <c>TestAuthHandler</c> returns
    /// NoResult), mirroring <c>FormSubmissionOutboxAtomicityTests.Submit_PublicPath_AnonymousHttp_
    /// Succeeds_EnqueuesExactlyOneOutboxRow</c> — the real anonymous-request <c>ITenantContext</c>
    /// path (TenantId resolved server-side from the template row), not a bare-DI-scope <c>ISender</c>
    /// call that would mask the real authz gate.
    /// </summary>
    [Fact]
    public async Task Submit_TableMode_PublicPath_AnonymousHttp_InsertsChildRowAndEnqueuesExactlyOneOutboxRow()
    {
        var admin = CreateClientWithPermissions(_app, AdminId, TenantId, PermCodes.Forms.Template.Manage);
        var code  = $"TBP{Guid.NewGuid():N}"[..9];

        var (templatePublicId, tableFieldId) = await CreateTableModeTemplateAsync(admin, code);
        var templateId = await MaterializeTableTemplateAsync(_app, templatePublicId, includeChildTables: true);

        // No auth headers -> anonymous principal -> real request-scoped ITenantContext (TenantId=null),
        // the real production authz path SubmitPublicFormCommandHandler runs behind.
        var anon = _app.CreateClient();
        var submitResp = await anon.PostAsJsonAsync($"/api/v1/public/forms/{code}/submit", new
        {
            data = new Dictionary<string, object?>
            {
                ["fullName"] = "Anonymous Citizen",
                ["items"] = new object[]
                {
                    new Dictionary<string, object?> { ["itemName"] = "Public Widget" }
                }
            }
        });

        submitResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "the anonymous public Table-mode submit must succeed end-to-end");
        var submissionId = (await submitResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetGuid();

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();

        var outboxRows = await db.OutboxMessages
            .Where(m => m.Payload.Contains(submissionId.ToString()))
            .ToListAsync();
        outboxRows.Should().HaveCount(1,
            "the anonymous public path must enqueue exactly one outbox row, same as the authenticated path");

        // The core regression guard: child rows must NOT be silently dropped on the public path anymore.
        var tableFieldDataRepo = scope.ServiceProvider.GetRequiredService<ITableFieldDataRepository>();
        var rows = await tableFieldDataRepo.GetRowsAsync(templateId, tableFieldId, submissionId);
        rows.Should().HaveCount(1,
            "PUBLIC-FORMS-TABLEFIELD-CHILDROW-DROP: the anonymous public path must persist TableField " +
            "child rows inside the same atomic span as the authenticated path, not silently drop them");
    }

    // --- Harness: template + fields ---

    /// <summary>
    /// Creates a Draft Table-mode template with one scalar Text parent field ("fullName") and one
    /// TableField ("items") containing one Text child field ("itemName"), all via the real HTTP
    /// API, then publishes it (Draft -&gt; Materializing). Returns the template's public id and the
    /// TableField's internal (long) id — callers pass the latter to
    /// <see cref="MaterializeTableTemplateAsync"/> and to query child rows back.
    /// </summary>
    private async Task<(Guid TemplatePublicId, long TableFieldId)>
        CreateTableModeTemplateAsync(HttpClient admin, string code)
    {
        var createResp = await admin.PostAsJsonAsync("/api/v1/forms/templates", new
        {
            tenantId = TenantId,
            name = $"Table Mode Atomicity {code}",
            nameVi = $"Table Mode Atomicity {code}",
            code,
            storageMode = 1, // StorageMode.Table (no JsonStringEnumConverter registered)
            createdBy = AdminId
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var templatePublicId = (await createResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        // Scalar parent field. Required=false to minimize submit-validation friction — the
        // atomicity proof only cares about the write span, not field-level validation rules.
        var addScalarResp = await admin.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/fields",
            new
            {
                tenantId = TenantId,
                fieldKey = "fullName",
                type = 0, // FormFieldType.Text
                labelEn = "Full Name",
                labelVi = "Họ tên",
                displayOrder = 1,
                required = false,
                updatedBy = AdminId
            });
        addScalarResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // TableField (parent).
        var addTableFieldResp = await admin.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/fields",
            new
            {
                tenantId = TenantId,
                fieldKey = "items",
                type = 20, // FormFieldType.TableField
                labelEn = "Items",
                labelVi = "Danh sách",
                displayOrder = 2,
                required = false,
                updatedBy = AdminId
            });
        addTableFieldResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var tableFieldId = (await addTableFieldResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetInt64();

        // Child field — Defect A regression guard: created via the real HTTP endpoint, passing
        // parentFieldId = the TableField's internal (bigint) FormField.Id resolved above.
        // AddFormFieldRequest.ParentFieldId now forwards through to AddFormFieldCommand.
        var addChildFieldResp = await admin.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/fields",
            new
            {
                tenantId = TenantId,
                fieldKey = "itemName",
                type = 0, // FormFieldType.Text
                labelEn = "Item Name",
                labelVi = "Tên mục",
                displayOrder = 1,
                required = false,
                updatedBy = AdminId,
                parentFieldId = tableFieldId
            });
        addChildFieldResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var publishResp = await admin.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/publish",
            new { tenantId = TenantId, updatedBy = AdminId });
        publishResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Set IsPublic=1 for public bypass read (by-Code resolution requires IsPublic + public_forms_bypass flag).
        await using var scope = _app.Services.CreateAsyncScope();
        var formsDb = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
        var internalId = await formsDb.FormTemplates
            .Where(t => t.PublicId == templatePublicId).Select(t => t.Id).FirstAsync();
        await formsDb.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE forms.FormTemplates SET IsPublic = 1 WHERE Id = {internalId}");

        return (templatePublicId, tableFieldId);
    }

    // --- Harness: synchronous materialization (WebAppFixture strips the real BackgroundService) ---

    /// <summary>
    /// Table-mode publish enqueues DDL materialization onto <c>FormMaterializationService</c>, a
    /// <c>BackgroundService</c> driven by <c>IMaterializationQueue</c>. <see cref="WebAppFixture"/>
    /// removes all <c>IHostedService</c> registrations (see
    /// feedback-webappfixture-removes-hostedservices), so that background service never runs in
    /// this test host — a Table-mode template would stay stuck in <c>Materializing</c> forever and
    /// could never be submitted against. This helper synchronously replicates
    /// <c>FormMaterializationService.MaterializeAsync</c>'s Steps 1-4 (CreateTableAsync -&gt;
    /// CreateOrReplaceViewAsync -&gt; [per-TableField child DDL] -&gt; MarkActive) using the real
    /// <see cref="IFormTableMigrator"/> — a normal DI-scoped service that IS present in the test
    /// host, unaffected by the IHostedService strip.
    ///
    /// <paramref name="includeChildTables"/> = false intentionally SKIPS Step 3's per-TableField
    /// child-table (+ child-view) DDL. This is the deliberate real-failure seam for the rollback
    /// test: the parent table/columns exist so <c>storage.WriteAsync</c> (the parent write)
    /// succeeds, but <c>ITableFieldDataRepository.InsertRowsAsync</c>'s INSERT targets a child
    /// table that was never created — a genuine SqlException, not a simulated one.
    ///
    /// Defect B regression guard: Step 2 (<c>CreateOrReplaceViewAsync</c>) and the child-view half
    /// of Step 3 (<c>CreateOrReplaceChildViewAsync</c>) now run for real (previously skipped while
    /// the join predicate was broken — see class doc). Immediately after each, this helper runs a
    /// <c>SELECT COUNT(*)</c> against the created view: if the operand-type-clash regression ever
    /// returns, that query throws a real SqlException here instead of the view silently never being
    /// exercised.
    /// </summary>
    private static async Task<long> MaterializeTableTemplateAsync(
        WebApplicationFactory<Program> app, Guid templatePublicId, bool includeChildTables)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var templateRepo = scope.ServiceProvider.GetRequiredService<IFormTemplateRepository>();
        var migrator = scope.ServiceProvider.GetRequiredService<IFormTableMigrator>();
        var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();

        var template = await templateRepo.GetByPublicIdWithFieldsAsync(
            templatePublicId, TenantId, CancellationToken.None);
        template.Should().NotBeNull();
        template!.Status.Should().Be(FormTemplateStatus.Materializing,
            "Table-mode publish must move the template to Materializing before this harness runs");

        var activeFields = template.Fields.Where(f => f.IsActive).ToList();

        // Step 1-2: parent dynamic table + BI view.
        await migrator.CreateTableAsync(template.Id, activeFields, CancellationToken.None);
        await migrator.CreateOrReplaceViewAsync(template.Code, template.Id, activeFields, CancellationToken.None);

        // Defect B guard — parent view must be queryable (no operand-type-clash regression).
        // EF1002 suppressed: the interpolated segment is a SQL object identifier (view name), not
        // user/request data — SqlQuery's parameterized FormattableString overload cannot be used
        // for identifiers (a parameter placeholder is not valid in place of a table/view name).
        // template.Code passed FormTemplate's own slug validation (ValidateCode) and the view was
        // just created via FormTableMigrator.GetViewName's identical sanitization; test `code`
        // values here are also generated as plain alphanumeric ($"TBH{Guid:N}"[..9]).
#pragma warning disable EF1002
        var parentViewCount = await db.Database
            .SqlQueryRaw<int>($"SELECT COUNT(*) AS Value FROM forms.[v_{template.Code}]")
            .FirstAsync(CancellationToken.None);
#pragma warning restore EF1002
        parentViewCount.Should().Be(0, "no submissions exist yet at materialization time");

        // Step 3: child table(s) + view(s) for each TableField — skipped when includeChildTables:false.
        if (includeChildTables)
        {
            var tableFields = activeFields.Where(f => f.Type == FormFieldType.TableField).ToList();
            foreach (var tableField in tableFields)
            {
                var childFields = activeFields.Where(f => f.ParentFieldId == tableField.Id).ToList();
                await migrator.CreateChildTableAsync(template.Id, tableField, childFields, CancellationToken.None);
                await migrator.CreateOrReplaceChildViewAsync(
                    template.Code, template.Id, tableField, childFields, CancellationToken.None);

                // Defect B guard — child view must be queryable too (same join-predicate fix).
                // EF1002 suppressed: same identifier-interpolation reasoning as the parent view guard above.
#pragma warning disable EF1002
                var childViewCount = await db.Database
                    .SqlQueryRaw<int>($"SELECT COUNT(*) AS Value FROM forms.[v_{template.Code}_{tableField.FieldKey}]")
                    .FirstAsync(CancellationToken.None);
#pragma warning restore EF1002
                childViewCount.Should().Be(0, "no child rows exist yet at materialization time");
            }
        }

        // Step 4: Materializing -> Active (the submit gate requires Status == Active).
        template.MarkActive(SystemUserId);
        await templateRepo.UpdateAsync(template, CancellationToken.None);

        return template.Id;
    }

    // --- Harness: auth client (mirrors FormSubmissionOutboxAtomicityTests) ---

    /// <summary>
    /// Mirrors <c>FormSubmissionOutboxAtomicityTests.CreateClientWithPermissions</c> — duplicated
    /// locally (that one is private to its class) and generalized to accept a caller-supplied
    /// factory.
    /// </summary>
    private static HttpClient CreateClientWithPermissions(
        WebApplicationFactory<Program> factory, Guid userId, Guid tenantId, params string[] permissions)
    {
        var permService = Substitute.For<IEffectivePermissionService>();
        permService
            .GetSummaryAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new EffectivePermissionSummary
            {
                UserId = userId,
                PermissionCodes = new HashSet<string>(permissions)
            });
        permService
            .GetSummaryAsync(Arg.Is<Guid>(id => id != userId), Arg.Any<CancellationToken>())
            .Returns(new EffectivePermissionSummary
            {
                UserId = Guid.Empty,
                PermissionCodes = new HashSet<string>()
            });

        var client = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddScoped(_ => permService)))
            .CreateClient();

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantIdHeader, tenantId.ToString());
        return client;
    }
}
