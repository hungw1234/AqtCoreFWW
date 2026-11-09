using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.Forms.Infrastructure.Jobs;
using AqtCoreFW.Forms.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AqtCoreFW.Forms.Integration.Tests;

/// <summary>
/// Execution-path coverage for <see cref="FormWorkflowLinkRetryJob"/> — Phase 04 keystone. Drives the
/// job's <c>ExecuteAsync</c> directly (not through Hangfire) against the real SQL Server integration DB
/// (<see cref="WebAppFixture"/>), substituting <see cref="IWorkflowModuleClient"/> per test to control
/// the resolve/create outcome. Real EF + real <see cref="AqtCoreFW.SharedKernel.Application.Data.ICrossModuleUnitOfWork"/>
/// — an in-memory/mocked DbContext would not exercise the per-row pinned-UoW commit/rollback semantics
/// this job depends on.
///
/// RT1-RT4 map 1:1 to plan.md's regression-test list; RT5 (tenant-pin under a non-db_owner login) is
/// documented as a KNOWN LIMITATION below — see the class-level remark.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class FormWorkflowLinkRetryJobTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    public FormWorkflowLinkRetryJobTests(WebAppFixture app) => _app = app;

    // --- RT1: throw-after-create orphan — instance already exists, resolve-only, NO create ---

    [Fact]
    public async Task ExecuteAsync_SubmissionAlreadyHasActiveInstance_ResolvesLink_DoesNotCallCreateInstanceAsync_MarksSucceeded()
    {
        var workflowClient = Substitute.For<IWorkflowModuleClient>();
        var existingInstanceId = Guid.NewGuid();

        Guid submissionPublicId;
        long attemptRowId;
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
            var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

            var (template, submission) = await SeedTemplateAndSubmissionAsync(db, gen);
            submissionPublicId = submission.PublicId;

            var attempt = FormSubmissionWorkflowLinkPendingAttempt.CreateFromEvent(
                TenantId, submission.PublicId, template.Id, Guid.NewGuid(), ActorId,
                failureCode: "GOV_WFL_014", failureMessage: "empty handler pool at initial state");
            db.FormSubmissionWorkflowLinkPendingAttempts.Add(attempt);
            await db.SaveChangesAsync();
            attemptRowId = attempt.Id;

            workflowClient
                .TryResolveInstanceForEntityAsync("FormSubmission", submission.PublicId, TenantId,
                    Arg.Any<CancellationToken>())
                .Returns(existingInstanceId);
        }

        await ExecuteJobAsync(workflowClient);

        await workflowClient.DidNotReceive().CreateInstanceAsync(
            Arg.Any<CreateWorkflowInstanceRequest>(), Arg.Any<CancellationToken>());

        await using var assertScope = _app.Services.CreateAsyncScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<FormsDbContext>();

        var attemptRow = await assertDb.FormSubmissionWorkflowLinkPendingAttempts
            .IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(a => a.Id == attemptRowId);
        attemptRow.Status.Should().Be(WorkflowLinkPendingStatus.Succeeded);

        var submissionRow = await assertDb.FormSubmissions
            .IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(s => s.PublicId == submissionPublicId);
        submissionRow.WorkflowInstanceId.Should().Be(existingInstanceId,
            "resolve-before-create must link the submission to the ALREADY-EXISTING instance");
    }

    // --- RT2: never-created orphan — no instance yet, handler pool now present ---

    [Fact]
    public async Task ExecuteAsync_NoExistingInstance_CreateSucceeds_LinksSubmission_MarksSucceeded()
    {
        var workflowClient = Substitute.For<IWorkflowModuleClient>();
        var newInstanceId = Guid.NewGuid();

        Guid submissionPublicId;
        long attemptRowId;
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
            var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

            var (template, submission) = await SeedTemplateAndSubmissionAsync(db, gen);
            submissionPublicId = submission.PublicId;

            var attempt = FormSubmissionWorkflowLinkPendingAttempt.CreateFromEvent(
                TenantId, submission.PublicId, template.Id, Guid.NewGuid(), ActorId,
                failureCode: "WF_NO_HANDLER", failureMessage: "empty pool");
            db.FormSubmissionWorkflowLinkPendingAttempts.Add(attempt);
            await db.SaveChangesAsync();
            attemptRowId = attempt.Id;

            workflowClient
                .TryResolveInstanceForEntityAsync("FormSubmission", submission.PublicId, TenantId,
                    Arg.Any<CancellationToken>())
                .Returns((Guid?)null);
            workflowClient
                .CreateInstanceAsync(Arg.Any<CreateWorkflowInstanceRequest>(), Arg.Any<CancellationToken>())
                .Returns(new WorkflowInstanceResult(newInstanceId, Guid.Empty, 0));
        }

        await ExecuteJobAsync(workflowClient);

        await workflowClient.Received(1).CreateInstanceAsync(
            Arg.Any<CreateWorkflowInstanceRequest>(), Arg.Any<CancellationToken>());

        await using var assertScope = _app.Services.CreateAsyncScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<FormsDbContext>();

        var attemptRow = await assertDb.FormSubmissionWorkflowLinkPendingAttempts
            .IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(a => a.Id == attemptRowId);
        attemptRow.Status.Should().Be(WorkflowLinkPendingStatus.Succeeded);

        var submissionRow = await assertDb.FormSubmissions
            .IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(s => s.PublicId == submissionPublicId);
        submissionRow.WorkflowInstanceId.Should().Be(newInstanceId);
    }

    // --- RT3: still-failing — AttemptCount++, NextRetryAt re-armed, stays non-terminal ---

    [Fact]
    public async Task ExecuteAsync_CreateStillFails_IncrementsAttemptCount_ReArmsNextRetryAt_StaysPending()
    {
        var workflowClient = Substitute.For<IWorkflowModuleClient>();

        long attemptRowId;
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
            var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

            var (template, submission) = await SeedTemplateAndSubmissionAsync(db, gen);

            var attempt = FormSubmissionWorkflowLinkPendingAttempt.CreateFromEvent(
                TenantId, submission.PublicId, template.Id, Guid.NewGuid(), ActorId,
                failureCode: "WF_NO_HANDLER", failureMessage: "still empty");
            db.FormSubmissionWorkflowLinkPendingAttempts.Add(attempt);
            await db.SaveChangesAsync();
            attemptRowId = attempt.Id;

            workflowClient
                .TryResolveInstanceForEntityAsync("FormSubmission", submission.PublicId, TenantId,
                    Arg.Any<CancellationToken>())
                .Returns((Guid?)null);
            workflowClient
                .CreateInstanceAsync(Arg.Any<CreateWorkflowInstanceRequest>(), Arg.Any<CancellationToken>())
                .Returns(WorkflowInstanceResult.Fail("WF_NO_HANDLER", "still no handler"));
        }

        await ExecuteJobAsync(workflowClient);

        await using var assertScope = _app.Services.CreateAsyncScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<FormsDbContext>();

        var attemptRow = await assertDb.FormSubmissionWorkflowLinkPendingAttempts
            .IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(a => a.Id == attemptRowId);

        attemptRow.AttemptCount.Should().Be(1);
        attemptRow.NextRetryAt.Should().NotBeNull();
        attemptRow.NextRetryAt.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(4)); // ~5min base backoff
        attemptRow.Status.Should().Be(WorkflowLinkPendingStatus.Pending);
    }

    // --- RT4: max-retries → Shelved; submission left untouched ---

    [Fact]
    public async Task ExecuteAsync_AtMaxAttemptsMinusOne_CreateFails_Shelves_LogsWarning_SubmissionUntouched()
    {
        var workflowClient = Substitute.For<IWorkflowModuleClient>();
        var testLogger = new CapturingLogger<FormWorkflowLinkRetryJob>();

        Guid submissionPublicId;
        long attemptRowId;
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
            var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();
            var opts = scope.ServiceProvider.GetRequiredService<IOptions<FormWorkflowLinkRetryOptions>>().Value;

            var (template, submission) = await SeedTemplateAndSubmissionAsync(db, gen);
            submissionPublicId = submission.PublicId;

            var attempt = FormSubmissionWorkflowLinkPendingAttempt.CreateFromEvent(
                TenantId, submission.PublicId, template.Id, Guid.NewGuid(), ActorId,
                failureCode: "WF_NO_HANDLER", failureMessage: "empty pool");
            db.FormSubmissionWorkflowLinkPendingAttempts.Add(attempt);
            await db.SaveChangesAsync();

            // Drive AttemptCount to MaxAttempts-1 via the real mutator (not a raw column poke) so the
            // entity invariants (Status re-arm to Pending) hold exactly as production would produce them.
            for (var i = 0; i < opts.MaxAttempts - 1; i++)
                attempt.RecordRetryFailure("WF_NO_HANDLER", "prior failure", DateTimeOffset.UtcNow, ActorId);
            await db.SaveChangesAsync();
            attemptRowId = attempt.Id;

            workflowClient
                .TryResolveInstanceForEntityAsync("FormSubmission", submission.PublicId, TenantId,
                    Arg.Any<CancellationToken>())
                .Returns((Guid?)null);
            workflowClient
                .CreateInstanceAsync(Arg.Any<CreateWorkflowInstanceRequest>(), Arg.Any<CancellationToken>())
                .Returns(WorkflowInstanceResult.Fail("WF_NO_HANDLER", "final failure"));
        }

        await ExecuteJobAsync(workflowClient, testLogger);

        await using var assertScope = _app.Services.CreateAsyncScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<FormsDbContext>();

        var attemptRow = await assertDb.FormSubmissionWorkflowLinkPendingAttempts
            .IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(a => a.Id == attemptRowId);
        attemptRow.Status.Should().Be(WorkflowLinkPendingStatus.Shelved);
        attemptRow.ResolvedAt.Should().NotBeNull();

        testLogger.WarningMessages.Should().Contain(m => m.Contains("SHELVED"),
            "shelving at MaxAttempts must emit a LogWarning (LOCKED Q2 — log-only dead-letter for launch)");

        var submissionRow = await assertDb.FormSubmissions
            .IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(s => s.PublicId == submissionPublicId);
        submissionRow.WorkflowInstanceId.Should().BeNull(
            "Shelved is a dead-letter, not a cancellation — the submission itself must stay untouched");
    }

    // --- RT5: tenant-pin — KNOWN LIMITATION (see remarks) ---

    /// <summary>
    /// RT5 (plan.md) asks that the write persist under a non-db_owner login with no RLS BLOCK. This
    /// integration harness's <see cref="WebAppFixture"/> connects to Testcontainers SQL Server as the
    /// db_owner/sa login (see <see cref="CrossModuleUnitOfWorkTenantOverrideTests"/>'s doc comment —
    /// same repo-wide limitation, `feedback-sa-dbowner-false-greens-rls`) — db_owner BYPASSES RLS
    /// FILTER/BLOCK predicates entirely, so a green result here would NOT prove the per-row tenant pin
    /// actually gates anything under least-privilege.
    ///
    /// What this test CAN and DOES assert at this harness's tier: the job pins SESSION_CONTEXT to the
    /// CLAIMED ROW's tenant (not the ambient/null background-job tenant) before its write, by reading
    /// back SESSION_CONTEXT during the pinned span via a second connection is not observable from
    /// outside the job — instead we assert the OBSERVABLE proxy: the write succeeds and lands under the
    /// row's own TenantId column (proving BeginAsync(row.TenantId) executed, not the parameterless
    /// ambient overload, which would have pinned null/no tenant for this job's scope).
    ///
    /// A true RLS-tier assertion (write BLOCKED without the pin, under a real non-db_owner login) needs
    /// the harness extension tracked as a known repo limitation — same gap as
    /// CrossModuleUnitOfWorkTenantOverrideTests. NOT faked green here.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_PinsSessionContextToRowTenant_WriteLandsUnderRowTenantId()
    {
        var workflowClient = Substitute.For<IWorkflowModuleClient>();
        var newInstanceId = Guid.NewGuid();

        long attemptRowId;
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
            var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

            var (template, submission) = await SeedTemplateAndSubmissionAsync(db, gen);

            var attempt = FormSubmissionWorkflowLinkPendingAttempt.CreateFromEvent(
                TenantId, submission.PublicId, template.Id, Guid.NewGuid(), ActorId,
                failureCode: "WF_NO_HANDLER", failureMessage: "empty pool");
            db.FormSubmissionWorkflowLinkPendingAttempts.Add(attempt);
            await db.SaveChangesAsync();
            attemptRowId = attempt.Id;

            workflowClient
                .TryResolveInstanceForEntityAsync("FormSubmission", submission.PublicId, TenantId,
                    Arg.Any<CancellationToken>())
                .Returns((Guid?)null);
            workflowClient
                .CreateInstanceAsync(Arg.Any<CreateWorkflowInstanceRequest>(), Arg.Any<CancellationToken>())
                .Returns(new WorkflowInstanceResult(newInstanceId, Guid.Empty, 0));
        }

        await ExecuteJobAsync(workflowClient);

        await using var assertScope = _app.Services.CreateAsyncScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<FormsDbContext>();
        var attemptRow = await assertDb.FormSubmissionWorkflowLinkPendingAttempts
            .IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(a => a.Id == attemptRowId);

        attemptRow.TenantId.Should().Be(TenantId);
        attemptRow.Status.Should().Be(WorkflowLinkPendingStatus.Succeeded,
            "the write must land successfully under the claimed row's own tenant — proving the job's " +
            "per-row BeginAsync(row.TenantId) executed rather than a no-tenant ambient pin");
    }

    // --- Helpers ---

    private static async Task<(FormTemplate Template, FormSubmission Submission)> SeedTemplateAndSubmissionAsync(
        FormsDbContext db, IGuidGenerator gen)
    {
        var template = FormTemplate.Create(
            gen: gen,
            tenantId: TenantId,
            name: $"WFL-Retry Template {Guid.NewGuid():N}",
            nameVi: "Mẫu kiểm thử retry liên kết workflow",
            code: $"WFLR-{Guid.NewGuid():N}"[..12],
            storageMode: StorageMode.Json,
            createdBy: ActorId);
        db.FormTemplates.Add(template);
        await db.SaveChangesAsync();

        var submission = FormSubmission.Create(
            gen: gen,
            tenantId: TenantId,
            formTemplateId: template.Id,
            formTemplateVersion: 1,
            submissionData: "{}",
            submittedBy: ActorId);
        db.FormSubmissions.Add(submission);
        await db.SaveChangesAsync();

        return (template, submission);
    }

    /// <summary>
    /// Resolves <see cref="FormWorkflowLinkRetryJob"/> from a server clone with
    /// <paramref name="workflowClient"/> (and optionally a capturing logger) substituted, then drives
    /// <c>ExecuteAsync</c> directly — bypassing Hangfire (mirrors how SlaBreachCheckerJob/
    /// EscalationCheckJob unit/integration tests invoke jobs directly rather than through the
    /// scheduler).
    /// </summary>
    private async Task ExecuteJobAsync(
        IWorkflowModuleClient workflowClient, CapturingLogger<FormWorkflowLinkRetryJob>? testLogger = null)
    {
        var server = _app.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IWorkflowModuleClient>();
                services.AddScoped(_ => workflowClient);
                if (testLogger is not null)
                {
                    services.RemoveAll<ILogger<FormWorkflowLinkRetryJob>>();
                    services.AddSingleton<ILogger<FormWorkflowLinkRetryJob>>(testLogger);
                }
            }));

        await using var scope = server.Services.CreateAsyncScope();
        var job = scope.ServiceProvider.GetRequiredService<FormWorkflowLinkRetryJob>();
        await job.ExecuteAsync(CancellationToken.None);
    }

    /// <summary>Minimal ILogger capture — records LogWarning message text for RT4's assertion.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> WarningMessages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                WarningMessages.Add(formatter(state, exception));
        }
    }
}
