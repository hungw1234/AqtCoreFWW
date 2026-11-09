using AqtCoreFW.Identity.Application.Commands.ManagePolicyRule;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Infrastructure.Commands.ManagePolicyRule;

/// <summary>Creates a new PolicyRule. Code must be unique.</summary>
public sealed class CreatePolicyRuleCommandHandler(IdentityDbContext db)
    : IRequestHandler<CreatePolicyRuleCommand, Result<long>>
{
    public async Task<Result<long>> Handle(CreatePolicyRuleCommand cmd, CancellationToken ct)
    {
        if (!Enum.TryParse<PolicyEffect>(cmd.Effect, ignoreCase: true, out var effect))
            return Result.Fail(new ValidationError($"Invalid effect '{cmd.Effect}'. Use 'Allow' or 'Deny'."));

        var codeExists = await db.PolicyRules.AnyAsync(r => r.Code == cmd.Code, ct);
        if (codeExists)
            return Result.Fail(new ConflictError($"Policy rule code '{cmd.Code}' already exists."));

        var rule = new PolicyRule
        {
            Code = cmd.Code,
            PermissionCode = cmd.PermissionCode,
            ConditionExpression = cmd.ConditionExpression,
            Effect = effect,
            Priority = cmd.Priority,
            LogOnDeny = cmd.LogOnDeny,
            Description = cmd.Description,
            TenantId = cmd.TenantId,
            IsActive = true
        };

        db.PolicyRules.Add(rule);
        await db.SaveChangesAsync(ct);
        return Result.Ok(rule.Id);
    }
}

/// <summary>Updates an existing PolicyRule — all fields except Code and TenantId.</summary>
public sealed class UpdatePolicyRuleCommandHandler(IdentityDbContext db)
    : IRequestHandler<UpdatePolicyRuleCommand, Result>
{
    public async Task<Result> Handle(UpdatePolicyRuleCommand cmd, CancellationToken ct)
    {
        var rule = await db.PolicyRules.FindAsync([cmd.Id], ct);
        if (rule is null)
            return Result.Fail(new NotFoundError($"PolicyRule {cmd.Id} not found."));

        if (!Enum.TryParse<PolicyEffect>(cmd.Effect, ignoreCase: true, out var effect))
            return Result.Fail(new ValidationError($"Invalid effect '{cmd.Effect}'. Use 'Allow' or 'Deny'."));

        rule.PermissionCode = cmd.PermissionCode;
        rule.ConditionExpression = cmd.ConditionExpression;
        rule.Effect = effect;
        rule.Priority = cmd.Priority;
        rule.IsActive = cmd.IsActive;
        rule.LogOnDeny = cmd.LogOnDeny;
        rule.Description = cmd.Description;

        await db.SaveChangesAsync(ct);
        return Result.Ok();
    }
}

/// <summary>Deletes a PolicyRule by ID.</summary>
public sealed class DeletePolicyRuleCommandHandler(IdentityDbContext db)
    : IRequestHandler<DeletePolicyRuleCommand, Result>
{
    public async Task<Result> Handle(DeletePolicyRuleCommand cmd, CancellationToken ct)
    {
        var rule = await db.PolicyRules.FindAsync([cmd.Id], ct);
        if (rule is null)
            return Result.Fail(new NotFoundError($"PolicyRule {cmd.Id} not found."));

        db.PolicyRules.Remove(rule);
        await db.SaveChangesAsync(ct);
        return Result.Ok();
    }
}
