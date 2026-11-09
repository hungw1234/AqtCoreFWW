using AqtCoreFW.Rules.Application.DTOs;
using AqtCoreFW.Rules.Domain.Entities;
using AqtCoreFW.Rules.Domain.Repositories;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Rules.Application.Commands.CreateDecisionTable;

public sealed class CreateDecisionTableCommandHandler(
    IDecisionTableRepository repository,
    IRuleSetRepository ruleSetRepository,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    ILogger<CreateDecisionTableCommandHandler> logger)
    : IRequestHandler<CreateDecisionTableCommand, Result<DecisionTableDto>>
{
    public async Task<Result<DecisionTableDto>> Handle(
        CreateDecisionTableCommand request,
        CancellationToken cancellationToken)
    {
        // Defence-in-depth: reject cross-tenant DecisionTable creation.
        if (request.TenantId != tenantContext.TenantId)
        {
            logger.LogWarning(
                "Cross-tenant DecisionTable create rejected: currentTenantId={CurrentTenantId}, attemptedTenantId={AttemptedTenantId}",
                tenantContext.TenantId, request.TenantId);
            return Result.Fail(new ValidationError("Cross-tenant DecisionTable create rejected"));
        }

        // request.RuleSetId is the parent RuleSet's Guid PublicId (T2 addressing).
        // Resolve internal long FK so DecisionTable.RuleSetId (long) is set correctly.
        var ruleSet = await ruleSetRepository.GetByPublicIdAsync(request.RuleSetId, cancellationToken);
        if (ruleSet is null)
            return Result.Fail(new NotFoundError($"RuleSet {request.RuleSetId} not found."));

        var table = DecisionTable.Create(
            ruleSet.Id,
            request.Name,
            request.InputColumns,
            request.OutputColumns,
            request.Rows,
            request.TenantId,
            currentUser.UserId);

        await repository.AddAsync(table, cancellationToken);

        // Domain entity stores JSON as raw strings; parse at the wire boundary so the response
        // matches the DecisionTableDto contract consumed by FE.
        // T1 → DTO Id is the long internal PK; RuleSetId field surfaces parent's Guid PublicId.
        return Result.Ok(new DecisionTableDto(
            table.Id, ruleSet.PublicId, table.TenantId, table.Name,
            DecisionTableJsonHelper.ParseColumns(table.InputColumns, logger, $"InputColumns:{table.Id}"),
            DecisionTableJsonHelper.ParseColumns(table.OutputColumns, logger, $"OutputColumns:{table.Id}"),
            DecisionTableJsonHelper.ParseRows(table.Rows, logger, $"Rows:{table.Id}"),
            table.CreatedAt, table.UpdatedAt));
    }
}
