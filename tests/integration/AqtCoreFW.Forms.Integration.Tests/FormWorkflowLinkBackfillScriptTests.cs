using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AqtCoreFW.Forms.Integration.Tests;

/// <summary>
/// Idempotency coverage for the ops backfill script
/// <c>scripts/backfill/forms-workflow-link-pending-backfill.sql</c> (Phase 07). Runs the ACTUAL
/// script file (read from disk, not a copy) against the real integration DB so a column-list drift
/// between the raw INSERT and the entity's insert shape (migration 20260522144724) fails the test —
/// the exact silent-failure class the script's "explicit columns, never SELECT *" rule guards against.
///
/// Two historical orphans are seeded; one already carries a Pending attempt row. Running the script
/// must arm EXACTLY the orphan that has no row yet (idempotency on the unique key
/// SubmissionId+TenantId); a second run must arm nothing.
///
/// Note: tests run as db_owner (sa), so RLS FILTER is bypassed — this exercises the column shape +
/// NOT-EXISTS idempotency, not tenant isolation (that is RT5's job).
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class FormWorkflowLinkBackfillScriptTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    public FormWorkflowLinkBackfillScriptTests(WebAppFixture app) => _app = app;

    [Fact]
    public async Task Backfill_ArmsOnlyOrphansWithoutAnExistingRow_AndIsIdempotentOnReRun()
    {
        Guid armedSubmissionId;   // orphan with NO pending row → should be armed
        Guid coveredSubmissionId; // orphan that ALREADY has a Pending row → must be skipped

        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
            var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

            var template = await SeedTemplateWithWorkflowAsync(db, gen);

            var orphanToArm = await SeedOrphanSubmissionAsync(db, gen, template.Id);
            var orphanAlreadyCovered = await SeedOrphanSubmissionAsync(db, gen, template.Id);
            armedSubmissionId = orphanToArm.PublicId;
            coveredSubmissionId = orphanAlreadyCovered.PublicId;

            // Pre-seed a pending row for the "covered" orphan so the backfill must skip it.
            var existing = FormSubmissionWorkflowLinkPendingAttempt.CreateFromEvent(
                TenantId, orphanAlreadyCovered.PublicId, template.Id,
                template.ApprovalWorkflowDefinitionId!.Value, ActorId,
                failureCode: "GOV_WFL_014", failureMessage: "pre-existing");
            db.FormSubmissionWorkflowLinkPendingAttempts.Add(existing);
            await db.SaveChangesAsync();
        }

        var script = await File.ReadAllTextAsync(ResolveBackfillScriptPath());

        // First run: exactly the un-covered orphan is armed.
        await ExecuteScriptAsync(script);
        (await CountBackfilledAsync(armedSubmissionId)).Should().Be(1,
            "the orphan without an existing row must be armed with a BACKFILL_ORPHAN Pending row");
        (await CountBackfilledAsync(coveredSubmissionId)).Should().Be(0,
            "the orphan that already had a pending row must NOT be re-inserted (idempotency key)");

        // Second run: nothing new (idempotent).
        await ExecuteScriptAsync(script);
        (await CountBackfilledAsync(armedSubmissionId)).Should().Be(1,
            "re-running the backfill must insert no duplicate for an already-armed orphan");
    }

    // --- Helpers ---

    private async Task ExecuteScriptAsync(string sql)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    private async Task<int> CountBackfilledAsync(Guid submissionId)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
        return await db.FormSubmissionWorkflowLinkPendingAttempts
            .IgnoreQueryFilters().AsNoTracking()
            .CountAsync(a => a.SubmissionId == submissionId
                          && a.TenantId == TenantId
                          && a.LastFailureCode == "BACKFILL_ORPHAN");
    }

    private static async Task<FormTemplate> SeedTemplateWithWorkflowAsync(FormsDbContext db, IGuidGenerator gen)
    {
        var template = FormTemplate.Create(
            gen: gen,
            tenantId: TenantId,
            name: $"WFL-Backfill Template {Guid.NewGuid():N}",
            nameVi: "Mẫu kiểm thử backfill liên kết workflow",
            code: $"WFLB-{Guid.NewGuid():N}"[..12],
            storageMode: StorageMode.Json,
            createdBy: ActorId);
        template.SetApprovalWorkflow(Guid.NewGuid(), ActorId); // makes submissions of this template orphans
        db.FormTemplates.Add(template);
        await db.SaveChangesAsync();
        return template;
    }

    private static async Task<FormSubmission> SeedOrphanSubmissionAsync(
        FormsDbContext db, IGuidGenerator gen, long templateId)
    {
        // FormSubmission.Create leaves WorkflowInstanceId null → orphan for a workflow-bound template.
        var submission = FormSubmission.Create(
            gen: gen,
            tenantId: TenantId,
            formTemplateId: templateId,
            formTemplateVersion: 1,
            submissionData: "{}",
            submittedBy: ActorId);
        db.FormSubmissions.Add(submission);
        await db.SaveChangesAsync();
        return submission;
    }

    /// <summary>Walk up from the test binary to the repo root, then locate the backfill script.</summary>
    private static string ResolveBackfillScriptPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        var path = Path.Combine(dir!.FullName, "scripts", "backfill",
            "forms-workflow-link-pending-backfill.sql");
        File.Exists(path).Should().BeTrue($"backfill script must exist at {path}");
        return path;
    }
}
