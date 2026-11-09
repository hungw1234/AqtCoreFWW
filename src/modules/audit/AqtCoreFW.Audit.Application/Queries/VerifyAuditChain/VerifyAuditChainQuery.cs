using AqtCoreFW.Audit.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Audit.Application.Queries.VerifyAuditChain;

/// <summary>
/// Query to verify HMAC chain integrity for a tenant's audit log.
/// TenantId is nullable so internal callers (e.g. backfill, health-check) can verify the
/// system-level chain (TenantId IS NULL). The public HTTP endpoint keeps a non-nullable
/// [FromQuery] Guid — null-tenant is never reachable from the controller's route binding.
/// </summary>
public sealed record VerifyAuditChainQuery(Guid? TenantId) : IRequest<Result<ChainVerificationResultDto>>;
