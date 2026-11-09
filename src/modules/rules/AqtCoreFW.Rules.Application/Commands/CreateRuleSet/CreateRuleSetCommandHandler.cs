using AqtCoreFW.Rules.Application.DTOs;
using AqtCoreFW.Rules.Domain.Entities;
using AqtCoreFW.Rules.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Domain.Events;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AqtCoreFW.Rules.Application.Commands.CreateRuleSet;

public sealed class CreateRuleSetCommandHandler(
    IRuleSetRepository repository,
    ICurrentUser currentUser,
    IDomainEventPublisher eventPublisher,
    ITenantContext tenantContext,
    IGuidGenerator guidGenerator,
    ILogger<CreateRuleSetCommandHandler> logger)
    : IRequestHandler<CreateRuleSetCommand, Result<RuleSetDto>>
{
    public async Task<Result<RuleSetDto>> Handle(
        CreateRuleSetCommand request,
        CancellationToken cancellationToken)
    {
        // Defence-in-depth: reject cross-tenant RuleSet creation.
        // request.TenantId comes from controller's ResolveTenantId() (JWT claim).
        // Malicious/buggy call path that supplies a different value is blocked here.
        if (request.TenantId != tenantContext.TenantId)
        {
            logger.LogWarning(
                "Cross-tenant RuleSet create rejected: currentTenantId={CurrentTenantId}, attemptedTenantId={AttemptedTenantId}",
                tenantContext.TenantId, request.TenantId);
            return Result.Fail(new ValidationError("Cross-tenant RuleSet create rejected"));
        }

        var ruleSet = RuleSet.Create(
            guidGenerator,
            request.Name,
            request.TenantId,
            currentUser.UserId,
            request.Description,
            request.Category,
            request.TargetEntityType);

        // Create initial version snapshot (empty rules at creation).
        // RuleVersion.RuleSetId FK is fixed up by EF on SaveChanges via the Versions navigation;
        // ruleSet.Id is 0 here pre-save, but EF resolves it from the parent on cascade-insert.
        var snapshot = JsonSerializer.Serialize(new { ruleSet.Name, ruleSet.Version, Rules = Array.Empty<object>() });
        var initialVersion = RuleVersion.Create(ruleSet.Id, ruleSet.Version, snapshot, currentUser.UserId);
        ruleSet.AddVersion(initialVersion);

        await repository.AddAsync(ruleSet, cancellationToken);

        await eventPublisher.PublishAsync(ruleSet.DomainEvents, cancellationToken);
        ruleSet.ClearDomainEvents();

        return Result.Ok(MapToDto(ruleSet));
    }

    /// <summary>Maps RuleSet aggregate → RuleSetDto. T2 → DTO Id mapped FROM PublicId.</summary>
    private static RuleSetDto MapToDto(RuleSet r) => new(
        r.PublicId, r.TenantId, r.Name, r.Description, r.Category,
        r.Version, r.Status.ToString(), r.TargetEntityType,
        r.CreatedAt, r.UpdatedAt,
        r.Rules.Select(rule => MapRuleToDto(rule, r.PublicId)).ToList());

    /// <summary>Maps Rule child entity → RuleDto. T2 → DTO Id mapped FROM PublicId; RuleSetId = parent PublicId.</summary>
    internal static RuleDto MapRuleToDto(Rule rule, Guid ruleSetPublicId) => new(
        rule.PublicId, ruleSetPublicId, rule.Name, rule.Expression,
        rule.Priority, rule.Enabled, rule.ErrorMessage, rule.FailureAction.ToString());
}
