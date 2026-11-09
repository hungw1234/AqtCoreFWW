using AqtCoreFW.Audit.Application.DTOs;

namespace AqtCoreFW.Audit.Application.Services;

/// <summary>
/// Abstracts HMAC chain verification so Application layer has no Infrastructure dependency.
/// Infrastructure provides the implementation via HmacChainService.
///
/// tenantId is nullable: null verifies the system-level chain (entries where TenantId IS NULL).
/// Exposing null-tenant verify to external callers is controlled at the controller layer —
/// the verify endpoint keeps a non-nullable [FromQuery] Guid so the NULL path is internal-only.
/// </summary>
public interface IHmacChainVerifier
{
    Task<ChainVerificationResultDto> VerifyAsync(Guid? tenantId, CancellationToken cancellationToken = default);
}
