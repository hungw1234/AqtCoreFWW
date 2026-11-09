using FluentResults;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using AqtCoreFW.SharedKernel.Application.Data;
using Dapper;

namespace AqtCoreFW.Identity.Application.SecurityHealth.Queries;

// ── Query ────────────────────────────────────────────────────────────────────

/// <summary>
/// QĐ 742 §1.5.a/1.5.b — D7 visibility: password complexity policy snapshot.
/// Returns both ASP.NET Identity PasswordOptions (framework defaults)
/// and the tenant's CredentialPolicy (project-level overrides).
/// </summary>
public sealed record GetPasswordPolicyStatusQuery(Guid TenantId)
    : IRequest<Result<PasswordPolicyStatusDto>>;

public sealed class GetPasswordPolicyStatusQueryValidator
    : AbstractValidator<GetPasswordPolicyStatusQuery>
{
    public GetPasswordPolicyStatusQueryValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
    }
}

// ── DTO ──────────────────────────────────────────────────────────────────────

/// <summary>
/// Snapshot of password policy for the D7 compliance visibility page.
/// Framework settings come from IOptions&lt;IdentityOptions&gt;;
/// project overrides come from CredentialPolicy.IsDefault for the tenant.
/// </summary>
public sealed record PasswordPolicyStatusDto(
    // ── Framework (ASP.NET Identity defaults) ────────────────
    bool FrameworkRequireDigit,
    bool FrameworkRequireLowercase,
    bool FrameworkRequireUppercase,
    bool FrameworkRequireNonAlphanumeric,
    int  FrameworkRequiredLength,
    // ── Project CredentialPolicy override ────────────────────
    bool   HasProjectPolicy,
    long?  PolicyId,
    string? PolicyName,
    int    MinLength,
    int    MaxLength,
    bool   RequireUppercase,
    bool   RequireLowercase,
    bool   RequireDigit,
    bool   RequireSpecialChar,
    /// <summary>QĐ 742 §1.5.b: days until mandatory rotation (0 = never).</summary>
    int    RotationDays,
    /// <summary>QĐ 742 §1.5.b: number of previous passwords blocked.</summary>
    int    PasswordHistoryCount);

// ── Handler ──────────────────────────────────────────────────────────────────

public sealed class GetPasswordPolicyStatusQueryHandler(
    IOptions<IdentityOptions> identityOptions,
    IReadDbConnection db)
    : IRequestHandler<GetPasswordPolicyStatusQuery, Result<PasswordPolicyStatusDto>>
{
    public async Task<Result<PasswordPolicyStatusDto>> Handle(
        GetPasswordPolicyStatusQuery request, CancellationToken ct)
    {
        var pw = identityOptions.Value.Password;

        const string sql = """
            SELECT TOP 1
                p.Id, p.Name,
                p.MinLength, p.MaxLength,
                p.RequireUppercase, p.RequireLowercase, p.RequireDigit, p.RequireSpecialChar,
                p.RotationDays, p.PasswordHistoryCount
            FROM [identity].CredentialPolicies p
            WHERE p.TenantId = @TenantId
              AND p.IsDefault = 1
              AND p.IsDeleted = 0
            """;

        var row = await db.QueryFirstOrDefaultAsync<PasswordPolicyRow>(
            sql, new { request.TenantId });

        return Result.Ok(new PasswordPolicyStatusDto(
            FrameworkRequireDigit:            pw.RequireDigit,
            FrameworkRequireLowercase:        pw.RequireLowercase,
            FrameworkRequireUppercase:        pw.RequireUppercase,
            FrameworkRequireNonAlphanumeric:  pw.RequireNonAlphanumeric,
            FrameworkRequiredLength:          pw.RequiredLength,
            HasProjectPolicy:   row is not null,
            PolicyId:           row?.Id,
            PolicyName:         row?.Name,
            MinLength:          row?.MinLength           ?? 0,
            MaxLength:          row?.MaxLength           ?? 0,
            RequireUppercase:   row?.RequireUppercase    ?? false,
            RequireLowercase:   row?.RequireLowercase    ?? false,
            RequireDigit:       row?.RequireDigit        ?? false,
            RequireSpecialChar: row?.RequireSpecialChar  ?? false,
            RotationDays:       row?.RotationDays        ?? 0,
            PasswordHistoryCount: row?.PasswordHistoryCount ?? 0));
    }
}

internal sealed record PasswordPolicyRow(
    long Id, string Name,
    int MinLength, int MaxLength,
    bool RequireUppercase, bool RequireLowercase,
    bool RequireDigit, bool RequireSpecialChar,
    int RotationDays, int PasswordHistoryCount);
