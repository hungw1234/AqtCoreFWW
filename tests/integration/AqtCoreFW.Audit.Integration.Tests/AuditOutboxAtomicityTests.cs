using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AqtCoreFW.Audit.Application.Services;
using AqtCoreFW.Audit.Infrastructure.Persistence;
using AqtCoreFW.Identity.Domain.Models;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Contracts.Events;
using AqtCoreFW.SharedKernel.Domain.Events;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace AqtCoreFW.Audit.Integration.Tests;

/// <summary>
/// Slice 1B-2 — proves AuditService (IAuditLogger/IAuditService) routes
/// <see cref="AuditLogAppendedIntegrationEvent"/> through the Outbox atomically (via
/// <c>IOutboxWriter</c> riding <c>ICrossModuleUnitOfWork</c>), replacing the former direct
/// <c>messageBus.PublishAsync</c> fire-and-log call — and that the H2 fix to
/// <c>CrossModuleUnitOfWork.BeginAsync</c> (reuse the ambient connection for a second sequential
/// cycle in the same scope) lets a real audited command (SubmitForm, which opens+commits its OWN
/// unit of work) still get auto-audited by AuditBehavior without throwing or leaking a connection.
///
/// Uses the REAL SQL Server integration DB (<see cref="WebAppFixture"/>) — NOT InMemory/mocked
/// EF, which would strip the real transaction semantics this atomicity proof depends on.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class AuditOutboxAtomicityTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    private static readonly string AppendedEventType =
        typeof(AuditLogAppendedIntegrationEvent).FullName!;

    public AuditOutboxAtomicityTests(WebAppFixture app) => _app = app;

    // --- Happy path: a direct IAuditLogger call enqueues exactly one outbox row ---

    [Fact]
    public async Task LogCommand_Success_EnqueuesExactlyOneOutboxRow_WithIdsOnlyPayload()
    {
        var tenantId = Guid.NewGuid();
        var action = $"TestAction{Guid.NewGuid():N}";

        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var auditLogger = scope.ServiceProvider.GetRequiredService<IAuditLogger>();
            await auditLogger.LogCommandAsync(
                action, "TestModule", "TestResource",
                resourceId: "res-1", commandType: null,
                dataSnapshot: "SHOULD_NOT_CROSS_THE_BUS",
                tenantIdOverride: tenantId);
        }

        await using var verifyScope = _app.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var entry = await db.AuditLogEntries.AsNoTracking()
            .SingleAsync(e => e.TenantId == tenantId && e.Action == action);

        var outboxRows = await db.OutboxMessages
            .Where(m => m.Payload.Contains(entry.MessageId.ToString()))
            .ToListAsync();

        outboxRows.Should().HaveCount(1, "exactly one outbox row must be enqueued for a successful audit append");
        var row = outboxRows[0];
        row.EventType.Should().Be(AppendedEventType,
            "the bare Type.FullName token — same convention OutboxInterceptor uses");
        row.SchemaName.Should().Be("audit");
        row.ProcessedAtUtc.Should().BeNull("dispatch is Slice 1A's concern — this test only proves it landed");

        // IDs-only payload — no PII: must NOT contain the dataSnapshot value (bus boundary rule).
        row.Payload.Should().NotContain("SHOULD_NOT_CROSS_THE_BUS");
        row.Payload.Should().Contain(tenantId.ToString());
    }

    // --- Atomicity: an outbox-enqueue failure rolls back the audit row too ---

    [Fact]
    public async Task LogCommand_OutboxEnqueueFails_RollsBackAuditRow_AndEnqueuesDurableRetry()
    {
        var tenantId = Guid.NewGuid();
        var action = $"TestAtomicityFail{Guid.NewGuid():N}";

        // Override IOutboxWriter — the seam AuditEntryPersister calls right after repository.AddAsync —
        // to force a failure INSIDE the write span. The atomic span must STILL roll back the audit row +
        // outbox together (atomicity unchanged), but the failure is no longer propagated to the caller:
        // AuditService now converts it into a durable Hangfire retry (cell #7 L1 — a post-commit audit
        // write is never silently lost). Substitute IBackgroundAuditJobEnqueuer to assert that hand-off.
        var enqueuer = Substitute.For<IBackgroundAuditJobEnqueuer>();
        var throwingApp = _app.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IOutboxWriter>();
                services.AddScoped<IOutboxWriter>(_ => new ThrowingOutboxWriter());
                services.RemoveAll<IBackgroundAuditJobEnqueuer>();
                services.AddSingleton(enqueuer);
            }));

        await using (var scope = throwingApp.Services.CreateAsyncScope())
        {
            var auditLogger = scope.ServiceProvider.GetRequiredService<IAuditLogger>();
            var act = async () => await auditLogger.LogCommandAsync(
                action, "TestModule", "TestResource",
                resourceId: null, commandType: null, dataSnapshot: null,
                tenantIdOverride: tenantId);

            // Fail-open: the write failure is caught at the source and converted to a durable retry —
            // NOT propagated. The pipeline must never break on an audit failure.
            await act.Should().NotThrowAsync();
        }

        // Atomicity preserved: the audit row + outbox enqueue still roll back together on the failure.
        await using var verifyScope = _app.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var entryCount = await db.AuditLogEntries
            .CountAsync(e => e.TenantId == tenantId && e.Action == action);
        entryCount.Should().Be(0,
            "a rolled-back transaction must not leave an orphan AuditLogEntry row — atomicity is unchanged");

        var outboxCount = await db.OutboxMessages
            .CountAsync(m => m.Payload.Contains(tenantId.ToString()));
        outboxCount.Should().Be(0, "no event may be enqueued for a row that never actually committed");

        // Durability: the lost write was handed to the retry lane, not silently dropped.
        enqueuer.Received(1).EnqueueIngestionRetry(
            Arg.Is<AuditIngestionIntent>(i => i.TenantId == tenantId && i.Action == action));
    }

    // --- Nesting: AuditService invoked under an already-active UoW joins it (no double-Begin) ---

    [Fact]
    public async Task LogCommand_UnderAlreadyActiveUow_JoinsAmbientTransaction_CommitsWithOuterCaller()
    {
        var tenantId = Guid.NewGuid();
        var action = $"TestNestedCommit{Guid.NewGuid():N}";

        await using var scope = _app.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<ICrossModuleUnitOfWork>();
        var auditLogger = scope.ServiceProvider.GetRequiredService<IAuditLogger>();

        await uow.BeginAsync();
        try
        {
            var act = async () => await auditLogger.LogCommandAsync(
                action, "TestModule", "TestResource",
                resourceId: null, commandType: null, dataSnapshot: null,
                tenantIdOverride: tenantId);

            await act.Should().NotThrowAsync(
                "AuditService must detect the ambient UoW is already active and join it (ownsUow=false) " +
                "instead of attempting a nested BeginAsync, which throws");

            await uow.CommitAsync();
        }
        catch
        {
            await uow.RollbackAsync();
            throw;
        }

        await using var verifyScope = _app.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var entry = await db.AuditLogEntries.AsNoTracking()
            .SingleOrDefaultAsync(e => e.TenantId == tenantId && e.Action == action);
        entry.Should().NotBeNull(
            "the outer caller's commit must persist the nested audit row too — same local SQL transaction");

        var outboxCount = await db.OutboxMessages
            .CountAsync(m => m.Payload.Contains(entry!.MessageId.ToString()));
        outboxCount.Should().Be(1, "the nested audit append must still enqueue its outbox row on the shared transaction");
    }

    [Fact]
    public async Task LogCommand_UnderAlreadyActiveUow_RollsBackWithOuterCaller()
    {
        var tenantId = Guid.NewGuid();
        var action = $"TestNestedRollback{Guid.NewGuid():N}";

        await using var scope = _app.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<ICrossModuleUnitOfWork>();
        var auditLogger = scope.ServiceProvider.GetRequiredService<IAuditLogger>();

        await uow.BeginAsync();
        await auditLogger.LogCommandAsync(
            action, "TestModule", "TestResource",
            resourceId: null, commandType: null, dataSnapshot: null,
            tenantIdOverride: tenantId);
        // Outer caller decides to roll back everything — the nested audit row must NOT survive.
        await uow.RollbackAsync();

        await using var verifyScope = _app.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var entryCount = await db.AuditLogEntries
            .CountAsync(e => e.TenantId == tenantId && e.Action == action);
        entryCount.Should().Be(0,
            "a nested audit append rides the OUTER caller's transaction — its rollback must roll back the audit row too");
    }

    // --- Regression: a real audited command (SubmitForm) opens+commits ITS OWN unit of work;
    // AuditBehavior then triggers a SECOND sequential cycle on the SAME scoped
    // CrossModuleUnitOfWork. Proves the H2 fix (BeginAsync reuses the still-open connection
    // instead of leaking a second one) — this is the first caller combination that exercises two
    // Begin/Commit cycles in one HTTP request. ---

    [Fact]
    public async Task SubmitForm_AuditBehaviorSecondUowCycle_CommitsBothOutboxRows()
    {
        var tenantId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var code = $"AUD{Guid.NewGuid():N}"[..9];

        var admin = CreateClientWithPermissions(adminId, tenantId, PermCodes.Forms.Template.Manage);
        var user = CreateClientWithPermissions(userId, tenantId,
            PermCodes.Forms.Submit.Create, PermCodes.Forms.Submit.View);

        var templatePublicId = await CreateActiveJsonTemplateAsync(admin, adminId, tenantId, code);

        var submitResp = await user.PostAsJsonAsync("/api/v1/forms/submissions", new
        {
            formTemplatePublicId = templatePublicId,
            data = new Dictionary<string, string> { ["fullName"] = "Audit Regression Test" }
        });

        submitResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "a second sequential CrossModuleUnitOfWork cycle (AuditBehavior, firing after SubmitForm's " +
            "own cycle already committed) must not throw 'already active' nor leak a connection");

        await using var scope = _app.Services.CreateAsyncScope();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var auditEntry = await auditDb.AuditLogEntries.AsNoTracking()
            .Where(e => e.TenantId == tenantId
                && e.Action == "Submit" && e.ModuleName == "Forms" && e.ResourceType == "Form")
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefaultAsync();

        auditEntry.Should().NotBeNull("AuditBehavior's second unit-of-work cycle must have appended the audit row");

        var auditOutboxCount = await auditDb.OutboxMessages
            .CountAsync(m => m.Payload.Contains(auditEntry!.MessageId.ToString()));
        auditOutboxCount.Should().Be(1, "the audit append's outbox row must have committed alongside it");
    }

    // --- Helpers ---

    private async Task<Guid> CreateActiveJsonTemplateAsync(
        HttpClient admin, Guid adminId, Guid tenantId, string code)
    {
        var createResp = await admin.PostAsJsonAsync("/api/v1/forms/templates", new
        {
            tenantId,
            name = $"Audit Regression {code}",
            nameVi = $"Audit Regression {code}",
            code,
            storageMode = 0, // StorageMode.Json (no JsonStringEnumConverter registered)
            createdBy = adminId
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var templatePublicId = (await createResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        var addFieldResp = await admin.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/fields",
            new
            {
                tenantId,
                fieldKey = "fullName",
                type = 0, // FormFieldType.Text
                labelEn = "Full Name",
                labelVi = "Họ tên",
                displayOrder = 1,
                required = true,
                updatedBy = adminId
            });
        addFieldResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var publishResp = await admin.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/publish",
            new { tenantId, updatedBy = adminId });
        publishResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        return templatePublicId;
    }

    /// <summary>Mirrors FormSubmissionOutboxAtomicityTests' local helper of the same shape.</summary>
    private HttpClient CreateClientWithPermissions(Guid userId, Guid tenantId, params string[] permissions)
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

        var client = _app.WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services =>
                    services.AddScoped(_ => permService)))
            .CreateClient();

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantIdHeader, tenantId.ToString());
        return client;
    }

    private sealed class ThrowingOutboxWriter : IOutboxWriter
    {
        public Task EnqueueAsync(IExternalDomainEvent evt, string schemaName, CancellationToken ct = default) =>
            throw new InvalidOperationException("Atomicity test seam — simulated outbox enqueue failure.");
    }
}
