using AqtCoreFW.Audit.Application.DTOs;
using AqtCoreFW.Audit.Application.Services;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Audit.Application.Queries.VerifyAuditChain;

/// <summary>
/// Delegates HMAC chain verification to IHmacChainVerifier (implemented by HmacChainService in Infrastructure).
/// F-05: compliance audit export verification — SystemAdmin only (enforced at controller).
/// </summary>
public sealed class VerifyAuditChainQueryHandler(IHmacChainVerifier verifier)
    : IRequestHandler<VerifyAuditChainQuery, Result<ChainVerificationResultDto>>
{
    public async Task<Result<ChainVerificationResultDto>> Handle(
        VerifyAuditChainQuery query, CancellationToken cancellationToken) =>
        Result.Ok(await verifier.VerifyAsync(query.TenantId, cancellationToken));
}
