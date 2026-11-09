using AqtCoreFW.Audit.Domain.Repositories;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Audit.Application.Commands.DeleteAuditRetentionPolicy;

public sealed class DeleteAuditRetentionPolicyCommandHandler(
    IAuditRetentionPolicyRepository repository,
    ITenantContext tenantContext)
    : IRequestHandler<DeleteAuditRetentionPolicyCommand, Result>
{
    public async Task<Result> Handle(
        DeleteAuditRetentionPolicyCommand request,
        CancellationToken cancellationToken)
    {
        var policy = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (policy is null)
            return Result.Fail(new NotFoundError($"AuditRetentionPolicy {request.Id} not found."));

        // Global default: TenantId=null AND Category=null — protected from deletion.
        // (Previously guarded by a Guid sentinel; now resolved by natural key per T1 long-Id tiering.)
        if (policy.TenantId is null && policy.Category is null)
            return Result.Fail(new BadRequestError(
                "Global default retention policy is protected — update instead.",
                "AUDIT_RETENTION_GLOBAL_DEFAULT_PROTECTED"));

        // Trust-boundary guard (code-review H1+H2): non-SystemAdmin callers must NOT delete
        // (a) policies belonging to another tenant or (b) global policies (TenantId=null).
        if (!tenantContext.IsSystemAdmin)
        {
            if (policy.TenantId is null)
                return Result.Fail(new ForbiddenError("Global retention policies are managed by SystemAdmin only."));
            if (policy.TenantId != tenantContext.TenantId)
                return Result.Fail(new ForbiddenError("Policy belongs to another tenant."));
        }

        byte[] caller;
        try { caller = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException)
        {
            return Result.Fail(new BadRequestError("RowVersion is not valid base64.", "AUDIT_RETENTION_BAD_ROWVERSION"));
        }
        if (caller.Length != 8)
            return Result.Fail(new BadRequestError("RowVersion must be 8 bytes.", "AUDIT_RETENTION_BAD_ROWVERSION"));
        if (!caller.AsSpan().SequenceEqual(policy.RowVersion))
            return Result.Fail(new ConflictError("Stale row — refresh and retry."));

        await repository.DeleteAsync(policy, cancellationToken);
        return Result.Ok();
    }
}
