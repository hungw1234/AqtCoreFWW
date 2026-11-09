using AqtCoreFW.Identity.Domain.Entities;

namespace AqtCoreFW.Identity.Domain.Repositories;

/// <summary>
/// Session repository — wraps OpenIddict token store for admin session management.
/// Returns raw token metadata; mapping to DTOs done in query handler.
/// </summary>
public interface ISessionRepository
{
    /// <summary>Returns active (non-expired, non-revoked) tokens, optionally filtered by user, with server-side pagination.</summary>
    Task<(IReadOnlyList<TokenInfo> Items, int TotalCount)> ListActiveAsync(
        Guid? userId, int page, int pageSize, CancellationToken ct = default);
}

/// <summary>Lightweight token metadata record — no PII beyond user id and IP.</summary>
public sealed record TokenInfo(
    string TokenId,
    Guid UserId,
    string? UserEmail,
    DateTime IssuedAt,
    DateTime ExpiresAt,
    string? IpAddress,
    string? ClientId);
