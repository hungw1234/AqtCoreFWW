using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using FluentValidation;
using MediatR;

namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>
/// Admin rejects a Pending signing-certificate enrollment with a reason. The domain transition sets
/// IsActive=false (H3) so the filtered unique serial index no longer blocks re-enrolling the same
/// serial. Resolved by <see cref="EnrollmentPublicId"/> (M1) and scoped to the admin's tenant
/// (<see cref="TenantId"/> from JWT — H4); a foreign-tenant enrollment resolves to NotFound.
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record RejectSignerCertEnrollmentCommand(
    Guid TenantId,
    Guid AdminUserId,
    Guid EnrollmentPublicId,
    string Reason) : ICommand;

public sealed class RejectSignerCertEnrollmentCommandValidator
    : AbstractValidator<RejectSignerCertEnrollmentCommand>
{
    public RejectSignerCertEnrollmentCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.AdminUserId).NotEmpty();
        RuleFor(x => x.EnrollmentPublicId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(512);
    }
}

public sealed class RejectSignerCertEnrollmentCommandHandler(ISignerCertEnrollmentRepository enrollments)
    : IRequestHandler<RejectSignerCertEnrollmentCommand, Result>
{
    public async Task<Result> Handle(RejectSignerCertEnrollmentCommand cmd, CancellationToken ct)
    {
        var found = await enrollments.GetByPublicIdAsync(cmd.TenantId, cmd.EnrollmentPublicId, ct);
        if (found is null)
            return Result.Fail(new NotFoundError($"Enrollment {cmd.EnrollmentPublicId} not found."));

        // Re-load TRACKED for mutation (the PublicId read is AsNoTracking per repo contract).
        var enrollment = await enrollments.GetByIdAsync(cmd.TenantId, found.Id, ct);
        if (enrollment is null)
            return Result.Fail(new NotFoundError($"Enrollment {cmd.EnrollmentPublicId} not found."));

        try
        {
            enrollment.Reject(cmd.AdminUserId, cmd.Reason); // domain throws if already terminal
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fail(new ConflictError(ex.Message));
        }

        await enrollments.SaveChangesAsync(ct);
        return Result.Ok();
    }
}
