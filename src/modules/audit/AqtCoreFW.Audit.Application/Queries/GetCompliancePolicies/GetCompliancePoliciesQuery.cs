using AqtCoreFW.Audit.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Audit.Application.Queries.GetCompliancePolicies;

/// <summary>Returns enabled compliance policies, optionally all if includeDisabled = true.</summary>
public sealed record GetCompliancePoliciesQuery(
    bool IncludeDisabled = false) : IRequest<Result<IReadOnlyList<CompliancePolicyDto>>>;
