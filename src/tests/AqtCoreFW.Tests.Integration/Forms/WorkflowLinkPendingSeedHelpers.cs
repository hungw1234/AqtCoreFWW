using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Tests.Integration.Forms;

/// <summary>
/// Seed helper for <see cref="FormSubmissionWorkflowLinkPendingAttempt"/> rows in the Phase-05
/// WorkflowLinkPendingController integration tests. No controller creates these rows directly (the
/// production write path is the <c>FormSubmissionWorkflowLinkPendingEventHandler</c> UPSERT triggered
/// by a submit-time orphan event) — tests seed via direct DbContext access, mirroring the
/// <c>WorkflowSeedHelpers.SeedFirstStateAssignmentByPermissionAsync</c> precedent.
///
/// A real <see cref="FormTemplate"/> row is seeded first — the list query INNER JOINs
/// forms.FormTemplates to retier FormTemplateId to the Guid PublicId AK (mirrors
/// ListFormSubmissionsQueryHandler's Phase-07 O-2 pattern), so a placeholder long FK with no
/// matching template row would silently vanish from every list result.
/// </summary>
internal static class WorkflowLinkPendingSeedHelpers
{
    private static async Task<long> SeedFormTemplateAsync(FormsDbContext ctx, IGuidGenerator guidGen, Guid tenantId, CancellationToken ct)
    {
        var template = FormTemplate.Create(
            guidGen, tenantId,
            name: "Workflow Link Pending Test Template",
            nameVi: "Mẫu kiểm tra kết nối quy trình",
            code: $"wflink_test_{Guid.NewGuid():N}"[..24],
            storageMode: StorageMode.Json,
            createdBy: Guid.NewGuid());

        ctx.FormTemplates.Add(template);
        await ctx.SaveChangesAsync(ct);
        return template.Id;
    }

    public static async Task<Guid> SeedPendingRowAsync(
        ApiFactory factory,
        Guid tenantId,
        Guid? submissionId = null,
        CancellationToken ct = default)
    {
        var id = submissionId ?? Guid.NewGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
        var guidGen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var formTemplateId = await SeedFormTemplateAsync(ctx, guidGen, tenantId, ct);

        var row = FormSubmissionWorkflowLinkPendingAttempt.CreateFromEvent(
            tenantId, id, formTemplateId, approvalWorkflowDefinitionId: Guid.NewGuid(),
            submittedBy: Guid.NewGuid(), failureCode: "GOV_WFL_014", failureMessage: "no handler pool");

        ctx.FormSubmissionWorkflowLinkPendingAttempts.Add(row);
        await ctx.SaveChangesAsync(ct);

        return id;
    }

    /// <summary>Seeds a row and immediately terminalizes it to Shelved (RT6a un-shelve fixture).</summary>
    public static async Task<Guid> SeedShelvedRowAsync(
        ApiFactory factory,
        Guid tenantId,
        Guid? submissionId = null,
        CancellationToken ct = default)
    {
        var id = submissionId ?? Guid.NewGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
        var guidGen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var formTemplateId = await SeedFormTemplateAsync(ctx, guidGen, tenantId, ct);

        var row = FormSubmissionWorkflowLinkPendingAttempt.CreateFromEvent(
            tenantId, id, formTemplateId, approvalWorkflowDefinitionId: Guid.NewGuid(),
            submittedBy: Guid.NewGuid(), failureCode: "GOV_WFL_014", failureMessage: "no handler pool");
        row.MarkShelved(Guid.NewGuid());

        ctx.FormSubmissionWorkflowLinkPendingAttempts.Add(row);
        await ctx.SaveChangesAsync(ct);

        return id;
    }
}
