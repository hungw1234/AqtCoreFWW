using AqtCoreFW.Files.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Files.Application.Queries.GetRetentionPolicies;

/// <summary>Returns retention policies for a tenant, optionally filtered by active status.</summary>
public sealed record GetRetentionPoliciesQuery(
    Guid TenantId,
    bool? IsActive = null) : IRequest<Result<IReadOnlyList<RetentionPolicyDto>>>;
