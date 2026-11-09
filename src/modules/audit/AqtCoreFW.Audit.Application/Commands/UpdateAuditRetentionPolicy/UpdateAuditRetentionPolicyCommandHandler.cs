using AqtCoreFW.Audit.Domain.Repositories;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Audit.Application.Commands.UpdateAuditRetentionPolicy;

public sealed class UpdateAuditRetentionPolicyCommandHandler(
    IAuditRetentionPolicyRepository repository,
    ITenantContext tenantContext)
    : IRequestHandler<UpdateAuditRetentionPolicyCommand, Result>
{
    public async Task<Result> Handle(
        UpdateAuditRetentionPolicyCommand request,
        CancellationToken cancellationToken)
    {
        var policy = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (policy is null)
            return Result.Fail(new NotFoundError($"AuditRetentionPolicy {request.Id} not found."));

        // Trust-boundary guard (code-review H1+H2): non-SystemAdmin callers must NOT mutate
        // (a) policies belonging to another tenant or (b) global policies (TenantId=null).
        // Without this, any tenant with RETENTION.MANAGE could PUT the global default and
        // affect every tenant's purge cadence.
        if (!tenantContext.IsSystemAdmin)
        {
            if (policy.TenantId is null)
                return Result.Fail(new ForbiddenError("Global retention policies are managed by SystemAdmin only."));
            if (policy.TenantId != tenantContext.TenantId)
                return Result.Fail(new ForbiddenError("Policy belongs to another tenant."));
        }

        // Optimistic concurrency: caller's base64 token must equal the row's current rowversion.
        // Compare BEFORE mutating so we surface a clean 409 instead of waiting for SaveChanges.
        byte[] caller;
        try { caller = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException)
        {
            return Result.Fail(new BadRequestError("RowVersion is not valid base64.", "AUDIT_RETENTION_BAD_ROWVERSION"));
        }
        // SQL Server rowversion is 8 bytes. Reject malformed lengths up-front (code-review M1)
        // so the caller gets a clear 400 instead of a 409 ("stale") that hides bad input.
        if (caller.Length != 8)
            return Result.Fail(new BadRequestError("RowVersion must be 8 bytes.", "AUDIT_RETENTION_BAD_ROWVERSION"));
        if (!caller.AsSpan().SequenceEqual(policy.RowVersion))
            return Result.Fail(new ConflictError("Stale row — refresh and retry."));

        try
        {
            policy.UpdateRetention(request.RetentionDays);
            if (request.ArchiveBeforePurge)
                policy.EnableArchive(request.ArchivePath ?? string.Empty);
            else
                policy.DisableArchive();
        }
        catch (ArgumentException ex)
        {
            return Result.Fail(new BadRequestError(ex.Message, "AUDIT_RETENTION_INVALID_INPUT"));
        }

        // EF concurrency exception is unlikely after precheck above; if a race fires
        // SaveChanges' own DbUpdateConcurrencyException, the global ProblemDetails
        // middleware translates to 500 — acceptable for the narrow race window.
        await repository.UpdateAsync(policy, cancellationToken);
        return Result.Ok();
    }
}
