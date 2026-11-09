using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace AqtCoreFW.Identity.Application.Signing.Profiles;

/// <summary>
/// Read model for a single one of the caller's signing-certificate enrollments. Surfaces the
/// anti-IDOR <see cref="PublicId"/>, provider provenance, approval lifecycle, and (for Rejected rows)
/// the admin's reason — everything the FE needs to render enrollment state without the OIDC claims.
/// </summary>
public sealed record SignatureEnrollmentStatusDto(
    Guid PublicId,
    EnrollmentKind Kind,
    CertApprovalStatus ApprovalStatus,
    bool IsActive,
    string SerialNumber,
    string Issuer,
    string? Subject,
    DateTime EnrolledAt,
    string? RejectionReason,
    CertSubjectType SubjectType,
    string? DisplayName)
{
    /// <summary>Projects a domain enrollment to its read model.</summary>
    public static SignatureEnrollmentStatusDto From(SignerCertEnrollment e) => new(
        PublicId:        e.PublicId,
        Kind:            e.Kind,
        ApprovalStatus:  e.ApprovalStatus,
        IsActive:        e.IsActive,
        SerialNumber:    e.SerialNumber,
        Issuer:          e.Issuer,
        Subject:         e.Subject,
        EnrolledAt:      e.EnrolledAt,
        RejectionReason: e.RejectionReason,
        SubjectType:     e.SubjectType,
        DisplayName:     e.DisplayName);
}

/// <summary>
/// Aggregate self-service view for the signature feature: the caller's profiles plus their enrollment
/// state. <see cref="NationalIdEstablished"/> tells the FE whether the prerequisite CCCD is set (a hard
/// gate for enrollment — see EnrollMySignatureCertCommand), and <see cref="HasApprovedEnrollment"/>
/// mirrors the binding guard's enrolled-tier rule (active + Approved) so the FE does not re-derive the
/// signing-unlock condition itself.
/// </summary>
public sealed record SignatureOverviewDto(
    bool NationalIdEstablished,
    bool HasApprovedEnrollment,
    IReadOnlyList<SignatureProfileDto> Profiles,
    IReadOnlyList<SignatureEnrollmentStatusDto> Enrollments);

/// <summary>
/// Self-service: returns the caller's signature profiles + enrollment status in one read. Scoped to
/// (TenantId, CallerUserId) injected from the JWT — a caller never sees another user's data. The user
/// lookup is tenant-pinned (AspNetUsers has no RLS query-filter) so a cross-tenant id resolves to 404.
/// </summary>
public sealed record SignatureOverviewQuery(
    Guid TenantId,
    Guid CallerUserId) : IQuery<SignatureOverviewDto>;

public sealed class SignatureOverviewQueryHandler(
    IUserSignatureProfileRepository profiles,
    ISignerCertEnrollmentRepository enrollments,
    UserManager<ApplicationUser> userManager)
    : IRequestHandler<SignatureOverviewQuery, Result<SignatureOverviewDto>>
{
    public async Task<Result<SignatureOverviewDto>> Handle(SignatureOverviewQuery query, CancellationToken ct)
    {
        // Tenant-pin the user: AspNetUsers is not RLS-filtered, so a foreign-tenant id must 404 here.
        var user = await userManager.FindByIdAsync(query.CallerUserId.ToString());
        if (user is null || user.TenantId != query.TenantId)
            return Result.Fail(new NotFoundError($"User {query.CallerUserId} not found."));

        var profileList = await profiles.ListByUserAsync(query.TenantId, query.CallerUserId, ct);
        // All statuses (active + revoked/rejected) — the FE renders the full enrollment history.
        var enrollmentList = await enrollments.GetByUserAsync(query.TenantId, query.CallerUserId, ct);

        var dto = new SignatureOverviewDto(
            NationalIdEstablished: !string.IsNullOrWhiteSpace(user.SigningNationalId),
            HasApprovedEnrollment: enrollmentList.Any(e =>
                e.IsActive && e.ApprovalStatus == CertApprovalStatus.Approved),
            Profiles:    profileList.Select(SignatureProfileDto.From).ToList(),
            Enrollments: enrollmentList.Select(SignatureEnrollmentStatusDto.From).ToList());

        return Result.Ok(dto);
    }
}
