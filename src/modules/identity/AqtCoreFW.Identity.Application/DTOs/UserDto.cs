using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Identity.Application.DTOs;

/// <summary>
/// User read DTO. SigningNationalId (CCCD, Confidential PII) is populated only on the single-user
/// detail read (GetUserById) and is nulled by UsersAdminController for callers lacking
/// IDENTITY.USER.UPDATE; list views never populate it. All consumers sit behind IDENTITY.USER.VIEW.
/// </summary>
public sealed record UserDto(
    Guid Id,
    string FullName,
    string Email,
    string? DepartmentCode,
    ClassificationLevel ClearanceLevel,
    bool IsActive,
    Guid? TenantId,
    DateTime CreatedAtUtc,
    DateTime? LastLoginAt,
    DateTime? PasswordExpiresAt,
    IReadOnlyList<string> Roles,
    // Signer's CCCD (Confidential PII) — populated only on GetUserById and only for callers with
    // IDENTITY.USER.UPDATE (controller-gated); null in list views. Prefills the edit-form field.
    string? SigningNationalId = null);

public sealed record RoleDto(
    Guid Id,
    string Name,
    string? Description,
    Guid? TenantId);

/// <summary>Result DTO for bulk user import operation.</summary>
public sealed record BulkImportResultDto(
    int SuccessCount,
    int FailedCount,
    IReadOnlyList<BulkImportRowError> Errors);

public sealed record BulkImportRowError(int RowNumber, string Email, string Reason);

/// <summary>Active session info for session management admin API.</summary>
public sealed record ActiveSessionDto(
    string TokenId,
    Guid UserId,
    string? UserEmail,
    DateTime IssuedAt,
    DateTime ExpiresAt,
    string? IpAddress,
    string? ClientId);

/// <summary>Access review pending item. Id = PublicId (T2 Guid AK).</summary>
public sealed record AccessReviewDto(
    Guid Id,
    Guid UserId,
    string? UserEmail,
    Guid RoleId,
    string? RoleName,
    DateTime NextReviewDue,
    DateTime CreatedAt);
