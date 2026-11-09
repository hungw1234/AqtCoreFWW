using AqtCoreFW.Identity.Application.Commands.ManageSodRule;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Infrastructure.Commands.ManageSodRule;

/// <summary>Creates a new SoD conflict rule. Pair (A, B, TenantId) must be unique.</summary>
public sealed class CreateSodRuleCommandHandler(IdentityDbContext db)
    : IRequestHandler<CreateSodRuleCommand, Result<long>>
{
    public async Task<Result<long>> Handle(CreateSodRuleCommand cmd, CancellationToken ct)
    {
        if (!Enum.TryParse<SodSeverity>(cmd.Severity, ignoreCase: true, out var severity))
            return Result.Fail(new ValidationError(
                $"Invalid Severity '{cmd.Severity}'. Use Low, Medium, or High."));

        var duplicate = await db.SodConflictRules.AnyAsync(r =>
            r.TenantId == cmd.TenantId
            && ((r.PermissionCodeA == cmd.PermissionCodeA && r.PermissionCodeB == cmd.PermissionCodeB)
             || (r.PermissionCodeA == cmd.PermissionCodeB && r.PermissionCodeB == cmd.PermissionCodeA)), ct);

        if (duplicate)
            return Result.Fail(new ConflictError(
                $"A SoD rule for ({cmd.PermissionCodeA}, {cmd.PermissionCodeB}) already exists."));

        var rule = new SodConflictRule
        {
            PermissionCodeA = cmd.PermissionCodeA,
            PermissionCodeB = cmd.PermissionCodeB,
            Severity = severity,
            Description = cmd.Description,
            TenantId = cmd.TenantId,
            IsActive = true
        };

        db.SodConflictRules.Add(rule);
        await db.SaveChangesAsync(ct);
        return Result.Ok(rule.Id);
    }
}

/// <summary>Updates an existing SoD conflict rule. Tenant-scoped lookup prevents cross-tenant mutation.</summary>
public sealed class UpdateSodRuleCommandHandler(IdentityDbContext db, ITenantContext tenantContext)
    : IRequestHandler<UpdateSodRuleCommand, Result>
{
    public async Task<Result> Handle(UpdateSodRuleCommand cmd, CancellationToken ct)
    {
        // Inline tenant filter — Identity module has NO global query filter.
        // Prevents user with SOD.MANAGE on tenant A mutating tenant B's rule via known ID.
        var rule = await db.SodConflictRules
            .FirstOrDefaultAsync(r => r.Id == cmd.Id && r.TenantId == tenantContext.TenantId, ct);
        if (rule is null)
            return Result.Fail(new NotFoundError($"SodConflictRule {cmd.Id} not found."));

        if (!Enum.TryParse<SodSeverity>(cmd.Severity, ignoreCase: true, out var severity))
            return Result.Fail(new ValidationError(
                $"Invalid Severity '{cmd.Severity}'. Use Low, Medium, or High."));

        rule.PermissionCodeA = cmd.PermissionCodeA;
        rule.PermissionCodeB = cmd.PermissionCodeB;
        rule.Severity = severity;
        rule.Description = cmd.Description;
        rule.IsActive = cmd.IsActive;

        await db.SaveChangesAsync(ct);
        return Result.Ok();
    }
}
