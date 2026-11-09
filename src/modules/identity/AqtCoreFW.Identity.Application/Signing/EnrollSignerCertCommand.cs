using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.Identity.Domain.Signing;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>
/// Enrols a signing certificate for a signer — the forge-resistant binding key (serial+issuer or
/// thumbprint) used by the cert==assigned-signer guard. Admin-driven write path; before any signer
/// is enrolled the guard falls back to the CCCD-from-subject bootstrap.
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record EnrollSignerCertCommand(
    Guid TenantId,
    Guid UserId,
    string SerialNumber,
    string Issuer,
    string Thumbprint,
    string? Subject,
    Guid EnrolledBy) : ICommand<long>;

public sealed class EnrollSignerCertCommandValidator : AbstractValidator<EnrollSignerCertCommand>
{
    public EnrollSignerCertCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.SerialNumber).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Issuer).NotEmpty().MaximumLength(512);
        RuleFor(x => x.Thumbprint).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Subject).MaximumLength(512);
        RuleFor(x => x.EnrolledBy).NotEmpty();
    }
}

public sealed class EnrollSignerCertCommandHandler(
    ISignerCertEnrollmentRepository repository,
    UserManager<ApplicationUser> userManager,
    IGuidGenerator guidGen)
    : IRequestHandler<EnrollSignerCertCommand, Result<long>>
{
    public async Task<Result<long>> Handle(EnrollSignerCertCommand cmd, CancellationToken ct)
    {
        // The signer must be a real user inside the enrolling tenant — otherwise an admin typo leaves
        // an orphan enrollment row bound to no one. A single NotFound for both "missing" and
        // "belongs to another tenant" avoids leaking cross-tenant existence to the SystemAdmin caller.
        var user = await userManager.FindByIdAsync(cmd.UserId.ToString());
        if (user is null || user.TenantId != cmd.TenantId)
            return Result.Fail(new NotFoundError($"Signer {cmd.UserId} not found."));

        // Reject a duplicate active enrollment (same serial+issuer for the same signer) up front so
        // the caller gets a clean conflict rather than a unique-index DbUpdateException.
        var active = await repository.GetActiveByUserAsync(cmd.TenantId, cmd.UserId, ct);
        var duplicate = active.Any(e =>
            string.Equals(e.SerialNumber, cmd.SerialNumber, StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.Issuer, cmd.Issuer, StringComparison.OrdinalIgnoreCase));
        if (duplicate)
            return Result.Fail(new BadRequestError(
                "This certificate is already enrolled for the signer.", "SIGNER_CERT_ALREADY_ENROLLED"));

        // Admin path: enrollment is immediately Approved (no self-service gate).
        // Kind defaults to RemoteViettelMysign for the admin-driven legacy path; self-service
        // handler (P2) will pass the appropriate Kind + Pending explicitly.
        var (subjectType, displayName) = VgcaCertSubjectParser.Classify(cmd.Subject);
        var enrollment = SignerCertEnrollment.Create(
            tenantId:       cmd.TenantId,
            userId:         cmd.UserId,
            serialNumber:   cmd.SerialNumber,
            issuer:         cmd.Issuer,
            thumbprint:     cmd.Thumbprint,
            subject:        cmd.Subject,
            enrolledBy:     cmd.EnrolledBy,
            kind:           EnrollmentKind.RemoteViettelMysign,
            approvalStatus: CertApprovalStatus.Approved,
            subjectType:    subjectType,
            displayName:    displayName,
            gen:            guidGen);

        await repository.AddAsync(enrollment, ct);
        return Result.Ok(enrollment.Id);
    }
}
