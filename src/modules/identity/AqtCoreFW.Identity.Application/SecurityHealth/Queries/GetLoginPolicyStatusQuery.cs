using AqtCoreFW.SharedKernel.Application.Data;
using Dapper;
using FluentResults;
using FluentValidation;
using MediatR;

namespace AqtCoreFW.Identity.Application.SecurityHealth.Queries;

// ── Query ────────────────────────────────────────────────────────────────────

/// <summary>
/// QĐ 742 §1.4.b/1.4.d — D7 visibility: login policy snapshot.
/// Returns the default CredentialPolicy for the tenant (lockout + escalation settings).
/// </summary>
public sealed record GetLoginPolicyStatusQuery(Guid TenantId)
    : IRequest<Result<LoginPolicyStatusDto>>;

public sealed class GetLoginPolicyStatusQueryValidator
    : AbstractValidator<GetLoginPolicyStatusQuery>
{
    public GetLoginPolicyStatusQueryValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
    }
}

// ── DTO ──────────────────────────────────────────────────────────────────────

/// <summary>
/// Snapshot of login / lockout policy for the D7 compliance visibility page.
/// Fields map 1-to-1 with QĐ 742 §1.4 requirements.
/// </summary>
public sealed record LoginPolicyStatusDto(
    /// <summary>Internal policy id (T1 entity).</summary>
    long? PolicyId,
    string? PolicyName,
    int MaxFailedAttempts,
    int LockoutMinutes,
    bool ShowRemainingAttempts,
    /// <summary>QĐ 742 1.4.d: consecutive lockout count before auto-disable.</summary>
    int EscalationLockoutCount,
    /// <summary>QĐ 742 1.4.d: sliding window in hours for escalation count.</summary>
    int EscalationWindowHours,
    /// <summary>True when a default policy is configured for this tenant.</summary>
    bool HasDefaultPolicy);

// ── Handler ──────────────────────────────────────────────────────────────────

public sealed class GetLoginPolicyStatusQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetLoginPolicyStatusQuery, Result<LoginPolicyStatusDto>>
{
    public async Task<Result<LoginPolicyStatusDto>> Handle(
        GetLoginPolicyStatusQuery request, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP 1
                p.Id, p.Name,
                p.MaxFailedAttempts, p.LockoutMinutes, p.ShowRemainingAttempts,
                p.EscalationLockoutCount, p.EscalationWindowHours
            FROM [identity].CredentialPolicies p
            WHERE p.TenantId = @TenantId
              AND p.IsDefault = 1
              AND p.IsDeleted = 0
            """;

        var row = await db.QueryFirstOrDefaultAsync<LoginPolicyRow>(
            sql, new { request.TenantId });

        if (row is null)
        {
            return Result.Ok(new LoginPolicyStatusDto(
                null, null, 0, 0, false, 0, 0, HasDefaultPolicy: false));
        }

        return Result.Ok(new LoginPolicyStatusDto(
            row.Id, row.Name,
            row.MaxFailedAttempts, row.LockoutMinutes, row.ShowRemainingAttempts,
            row.EscalationLockoutCount, row.EscalationWindowHours,
            HasDefaultPolicy: true));
    }
}

internal sealed record LoginPolicyRow(
    long Id, string Name,
    int MaxFailedAttempts, int LockoutMinutes, bool ShowRemainingAttempts,
    int EscalationLockoutCount, int EscalationWindowHours);
