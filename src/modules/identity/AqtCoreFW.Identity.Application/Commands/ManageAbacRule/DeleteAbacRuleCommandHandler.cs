using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Identity.Application.Authorization;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Domain;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Identity.Application.Commands.ManageAbacRule;

public sealed class DeleteAbacRuleCommandHandler(
    IAttributeRuleRepository repository,
    IAbacCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext,
    ILogger<DeleteAbacRuleCommandHandler> logger)
    : IRequestHandler<DeleteAbacRuleCommand, Result>
{
    public async Task<Result> Handle(DeleteAbacRuleCommand cmd, CancellationToken ct)
    {
        // Fetch BEFORE delete Ä‘á»ƒ capture TenantId + AttributeValue cho precise invalidation.
        // GetByIdAsync Ä‘Ã£ tenant-filter (B.1) â†’ cross-tenant ID tráº£ null â†’ silent no-op (match current behavior).
        var rule = await repository.GetByIdAsync(cmd.RuleId, ct);
        if (rule is null)
        {
            // Audit trail cho cross-tenant attempt â€” symmetric vá»›i UpdateAbacRule hardening (v2.121 FU2).
            // Logs caller's tenant context Ä‘á»ƒ distinguish legitimate retry vs cross-tenant probe á»Ÿ forensic review.
            logger.LogWarning(
                "AbacRule {RuleId} not found for delete â€” currentTenantId={CurrentTenantId}. Possible cross-tenant attempt or already-deleted rule.",
                cmd.RuleId, tenantContext.TenantId);
            return Result.Ok(); // Preserve silent no-op semantics.
        }

        await repository.DeleteAsync(cmd.RuleId, ct);

        // Precise invalidation â€” no more nuclear Compact(1.0).
        await cacheInvalidator.InvalidateAsync(rule.TenantId, rule.AttributeValue, ct);

        return Result.Ok();
    }
}
