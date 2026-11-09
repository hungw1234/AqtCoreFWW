using AqtCoreFW.Audit.Domain.Enums;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Audit.Application.Commands.CreateCompliancePolicy;

public sealed record CreateCompliancePolicyCommand(
    string Name,
    ComplianceCategory Category,
    string Rules,
    PolicyEnforcement Enforcement) : IRequest<Result<long>>;
