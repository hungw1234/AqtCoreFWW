using AqtCoreFW.Audit.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Audit.Application.Queries.GetAuditRetentionPolicies;

/// <summary>
/// List retention policies for the caller's tenant scope plus global defaults.
/// SystemAdmin: pass <c>tenantIdFilter == null</c> to see all rows across tenants.
/// </summary>
public sealed record GetAuditRetentionPoliciesQuery(Guid? TenantIdFilter)
    : IRequest<Result<IReadOnlyList<AuditRetentionPolicyDto>>>;
