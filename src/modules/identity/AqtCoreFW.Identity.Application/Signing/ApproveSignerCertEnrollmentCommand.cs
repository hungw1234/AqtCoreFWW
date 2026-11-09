using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using FluentValidation;
using MediatR;

namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>
/// Admin approves a Pending signing-certificate enrollment, transitioning it to Approved so it can
/// satisfy the enrolled-tier binding check. Resolved by <see cref="EnrollmentPublicId"/> (M1 — anti-IDOR,
/// never the enumerable long id) and scoped to the admin's own tenant (<see cref="TenantId"/> read
/// STRICTLY from the JWT claim — H4): an enrollment in another tenant resolves to NotFound (no existence
/// leak). The cross-tenant guarantee lives here because AspNetUsers/enrollment lookups are tenant-pinned
/// in code, not via the X-Tenant-Id-honouring <c>ResolveTenantId</c>.
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record ApproveSignerCertEnrollmentCommand(
    Guid TenantId,
    Guid AdminUserId,
    Guid EnrollmentPublicId) : ICommand;

public sealed class ApproveSignerCertEnrollmentCommandValidator
    : AbstractValidator<ApproveSignerCertEnrollmentCommand>
{
    public ApproveSignerCertEnrollmentCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.AdminUserId).NotEmpty();
        RuleFor(x => x.EnrollmentPublicId).NotEmpty();
    }
}

public sealed class ApproveSignerCertEnrollmentCommandHandler(ISignerCertEnrollmentRepository enrollments)
    : IRequestHandler<ApproveSignerCertEnrollmentCommand, Result>
{
    public async Task<Result> Handle(ApproveSignerCertEnrollmentCommand cmd, CancellationToken ct)
    {
        // Resolve+authorize by PublicId scoped to the admin's tenant (no-track read). A foreign-tenant
        // PublicId returns null → NotFound (cross-tenant IDOR-safe, no existence leak).
        var found = await enrollments.GetByPublicIdAsync(cmd.TenantId, cmd.EnrollmentPublicId, ct);
        if (found is null)
            return Result.Fail(new NotFoundError($"Enrollment {cmd.EnrollmentPublicId} not found."));

        // Re-load TRACKED for mutation (the PublicId read is AsNoTracking per repo contract).
        var enrollment = await enrollments.GetByIdAsync(cmd.TenantId, found.Id, ct);
        if (enrollment is null)
            return Result.Fail(new NotFoundError($"Enrollment {cmd.EnrollmentPublicId} not found."));

        try
        {
            enrollment.Approve(cmd.AdminUserId); // domain throws if already terminal (Approved/Rejected)
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fail(new ConflictError(ex.Message));
        }

        await enrollments.SaveChangesAsync(ct);
        return Result.Ok();
    }
}
