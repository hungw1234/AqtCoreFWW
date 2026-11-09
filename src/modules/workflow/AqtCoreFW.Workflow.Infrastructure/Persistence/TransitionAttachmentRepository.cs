using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;

namespace AqtCoreFW.Workflow.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of ITransitionAttachmentRepository.
/// Appends attachment records — no update/delete path needed (audit-safe).
/// </summary>
public sealed class TransitionAttachmentRepository(WorkflowDbContext ctx)
    : ITransitionAttachmentRepository
{
    public async Task AddRangeAsync(
        IEnumerable<WorkflowTransitionAttachment> attachments,
        CancellationToken cancellationToken = default)
    {
        await ctx.TransitionAttachments.AddRangeAsync(attachments, cancellationToken);
        // SaveChanges is NOT called here — caller controls the unit of work transaction.
    }
}
