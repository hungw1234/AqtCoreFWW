using AqtCoreFW.Api;
using AqtCoreFW.Forms.Application.Commands.SubmitPublicForm;
using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Infrastructure.Persistence;
using AqtCoreFW.Identity.Domain.Models;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.SharedKernel.Application.Pagination;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Contracts.Events;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AqtCoreFW.Forms.Integration.Tests;

/// <summary>
/// Slice 1B-1 — proves SubmitForm(CommandHandler) + SubmitPublicFormCommandHandler route
/// <see cref="FormSubmissionAcknowledgedIntegrationEvent"/> through the Outbox atomically (via
/// <c>IOutboxWriter</c> riding <c>ICrossModuleUnitOfWork</c>), replacing the former direct
/// <c>messageBus.PublishAsync</c> fire-and-log call.
///
/// Uses the REAL SQL Server integration DB (<see cref="WebAppFixture"/>) — NOT InMemory/mocked
/// EF, which would strip the real transaction semantics this atomicity proof depends on.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class FormSubmissionOutboxAtomicityTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid AdminId  = Guid.NewGuid();
    private static readonly Guid UserId   = Guid.NewGuid();

    private static readonly string AcknowledgedEventType =
        typeof(FormSubmissionAcknowledgedIntegrationEvent).FullName!;

    public FormSubmissionOutboxAtomicityTests(WebAppFixture app) => _app = app;

    // --- Happy path: authenticated submit ---

    [Fact]
    public async Task Submit_Success_EnqueuesExactlyOneOutboxRow_WithIdsOnlyPayload()
    {
        var admin = CreateClientWithPermissions(_app, AdminId, TenantId, PermCodes.Forms.Template.Manage);
        var user  = CreateClientWithPermissions(_app, UserId, TenantId,
            PermCodes.Forms.Submit.Create, PermCodes.Forms.Submit.View);
        var code  = $"OBX{Guid.NewGuid():N}"[..9];

        var templatePublicId = await CreateActiveJsonTemplateAsync(admin, code);

        var submitResp = await user.PostAsJsonAsync("/api/v1/forms/submissions", new
        {
            formTemplatePublicId = templatePublicId,
            data = new Dictionary<string, string> { ["fullName"] = "Nguyen Van A" }
        });

        submitResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var submissionId = (await submitResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetGuid();

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
        var rows = await db.OutboxMessages
            .Where(m => m.Payload.Contains(submissionId.ToString()))
            .ToListAsync();

        rows.Should().HaveCount(1, "exactly one outbox row must be enqueued for a successful submit");
        var row = rows[0];
        row.EventType.Should().Be(AcknowledgedEventType,
            "the bare Type.FullName token — same convention OutboxInterceptor uses");
        row.SchemaName.Should().Be("forms");
        row.ProcessedAtUtc.Should().BeNull("dispatch is Slice 1A's concern — this test only proves it landed");

        // IDs-only payload — no PII: must NOT contain the submitted field's actual value.
        row.Payload.Should().NotContain("Nguyen Van A");
        row.Payload.Should().Contain(templatePublicId.ToString());
        row.Payload.Should().Contain(TenantId.ToString());
        row.Payload.Should().Contain(UserId.ToString());
    }

    // --- Happy path: anonymous public submit via the REAL public HTTP endpoint ---

    /// <summary>
    /// Anonymous <c>POST /api/v1/public/forms/{code}/submit</c> succeeds end-to-end and enqueues exactly
    /// one outbox row — the real handler + real DbContext + real CrossModuleUnitOfWork + real IOutboxWriter.
    ///
    /// Regression guard for FORMS-PUBLIC-SUBMIT-500: before the fix this 500'd because
    /// <c>SubmitPublicFormCommand</c> did not implement <c>ITenantAgnosticCommand</c>, so
    /// <c>TenantAwareBehavior</c> rejected the anonymous request (no ambient tenant — the whole point of a
    /// public endpoint; the tenant is resolved server-side from the template row). The command now opts
    /// out of ambient-tenant enforcement and the handler pins SESSION_CONTEXT to the template-resolved
    /// tenant before the RLS-protected write.
    ///
    /// Uses a bare anonymous <c>HttpClient</c> (NO auth headers → <c>TestAuthHandler</c> returns
    /// NoResult) so <c>ITenantContext</c> is the real request-scoped anonymous context
    /// (TenantId=null, IsSystemAdmin=false) — the real production authz path for the
    /// <c>TenantAwareBehavior</c> gate. The former version drove the handler via a bare-DI-scope
    /// <c>ISender</c>, whose <c>BackgroundJobTenantContext</c> (IsSystemAdmin=true) masked this very
    /// gate — a false-green we deliberately removed.
    ///
    /// SCOPE: this proves the PIPELINE-GATE fix only. The integration DB connects as db_owner, so the
    /// RLS FILTER/BLOCK predicates on forms.FormTemplates/FormSubmissions are bypassed here — the
    /// least-privilege RLS tier (anonymous template-read FILTER + submission-write BLOCK) is NOT
    /// covered and is tracked separately (PUBLIC-FORMS-LEAST-PRIV-ANON-READ).
    /// </summary>
    [Fact]
    public async Task Submit_PublicPath_AnonymousHttp_Succeeds_EnqueuesExactlyOneOutboxRow()
    {
        var admin = CreateClientWithPermissions(_app, AdminId, TenantId, PermCodes.Forms.Template.Manage);
        var code  = $"PUB{Guid.NewGuid():N}"[..9];

        var templatePublicId = await CreateActiveJsonTemplateAsync(admin, code);

        // No auth headers → anonymous principal → request-scoped ITenantContext (TenantId=null).
        var anon = _app.CreateClient();
        var submitResp = await anon.PostAsJsonAsync(
            $"/api/v1/public/forms/{code}/submit",
            new { data = new Dictionary<string, object?> { ["fullName"] = "Anonymous Citizen" } });

        submitResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "the anonymous public submit must no longer 500 on the TenantAwareBehavior gate");
        var submissionId = (await submitResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetGuid();

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
        var rows = await db.OutboxMessages
            .Where(m => m.Payload.Contains(submissionId.ToString()))
            .ToListAsync();

        rows.Should().HaveCount(1);
        rows[0].EventType.Should().Be(AcknowledgedEventType);
        rows[0].SchemaName.Should().Be("forms");
        // IDs-only payload — no PII value; tenant is the server-resolved template tenant.
        rows[0].Payload.Should().NotContain("Anonymous Citizen");
        rows[0].Payload.Should().Contain(templatePublicId.ToString());
        rows[0].Payload.Should().Contain(TenantId.ToString());
    }

    // --- Atomicity: a storage write failure after the submission add rolls back BOTH ---

    [Fact]
    public async Task Submit_StorageWriteFails_RollsBackSubmissionAndOutboxRow()
    {
        var admin = CreateClientWithPermissions(_app, AdminId, TenantId, PermCodes.Forms.Template.Manage);
        var code  = $"RBK{Guid.NewGuid():N}"[..9];

        var templatePublicId = await CreateActiveJsonTemplateAsync(admin, code);

        // Override IFormDataStorageFactory — the SAME seam SubmitFormCommandHandler always calls
        // right after submissionRepository.AddAsync (even for Json mode, where the real
        // implementation is normally a no-op) — to force a failure INSIDE the write span and
        // prove the whole transaction (submission + outbox enqueue) rolls back together.
        var throwingApp = _app.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IFormDataStorageFactory>();
                services.AddScoped<IFormDataStorageFactory>(_ => new ThrowingFormDataStorageFactory());
            }));

        var user = CreateClientWithPermissions(throwingApp, UserId, TenantId,
            PermCodes.Forms.Submit.Create, PermCodes.Forms.Submit.View);

        var submitResp = await user.PostAsJsonAsync("/api/v1/forms/submissions", new
        {
            formTemplatePublicId = templatePublicId,
            data = new Dictionary<string, string> { ["fullName"] = "Should Not Persist" }
        });

        submitResp.StatusCode.Should().Be(HttpStatusCode.InternalServerError,
            "the storage write throws unhandled — GlobalExceptionHandlerMiddleware maps it to 500; " +
            "same behavior as before this slice, now the write span is atomic instead of partially committed");

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();

        var templateInternalId = await db.FormTemplates
            .Where(t => t.PublicId == templatePublicId)
            .Select(t => t.Id)
            .FirstAsync();

        var submissionCount = await db.FormSubmissions
            .IgnoreQueryFilters()
            .CountAsync(s => s.FormTemplateId == templateInternalId);
        submissionCount.Should().Be(0,
            "a rolled-back transaction must not leave an orphan FormSubmission row — the C2 bug this slice fixes");

        var outboxCount = await db.OutboxMessages
            .CountAsync(m => m.Payload.Contains(templatePublicId.ToString()));
        outboxCount.Should().Be(0,
            "no event may be enqueued for a submission that never actually committed");
    }

    // --- Helpers ---

    private async Task<Guid> CreateActiveJsonTemplateAsync(HttpClient admin, string code)
    {
        var createResp = await admin.PostAsJsonAsync("/api/v1/forms/templates", new
        {
            tenantId = TenantId,
            name = $"Outbox Atomicity {code}",
            nameVi = $"Outbox Atomicity {code}",
            code,
            storageMode = 0, // StorageMode.Json (no JsonStringEnumConverter registered)
            createdBy = AdminId
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var templatePublicId = (await createResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        var addFieldResp = await admin.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/fields",
            new
            {
                tenantId = TenantId,
                fieldKey = "fullName",
                type = 0, // FormFieldType.Text
                labelEn = "Full Name",
                labelVi = "Họ tên",
                displayOrder = 1,
                required = true,
                updatedBy = AdminId
            });
        addFieldResp.StatusCode.Should().Be(HttpStatusCode.OK);

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

        return templatePublicId;
    }

    /// <summary>
    /// Mirrors <c>FormEndpointsTests.CreateClientWithPermissions</c> — duplicated locally (that
    /// one is private to its class) and generalized to accept a caller-supplied factory, since
    /// the atomicity test needs it against a <c>WithWebHostBuilder</c>-overridden factory, not
    /// just the base fixture.
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

    private sealed class ThrowingFormDataStorageFactory : IFormDataStorageFactory
    {
        public IFormDataStorage Create(StorageMode mode) => new ThrowingFormDataStorage();
    }

    private sealed class ThrowingFormDataStorage : IFormDataStorage
    {
        public Task WriteAsync(long templateId, Guid submissionId, Guid tenantId, int version,
            IDictionary<string, object?> values, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Atomicity test seam — simulated storage write failure.");

        public Task<IDictionary<string, object?>?> ReadAsync(
            long templateId, Guid submissionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PagedResult<IDictionary<string, object?>>> QueryAsync(
            long templateId, Guid tenantId, int page, int pageSize, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AnonymizeAsync(long templateId, IReadOnlyList<Guid> submissionIds,
            IReadOnlyList<string> piiFieldKeys, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
