using AqtCoreFW.Audit.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Audit.Application.Commands.EvaluateCompliancePolicy;

public sealed record EvaluateCompliancePolicyCommand(
    long PolicyId,
    string EntityType,
    Guid EntityId) : IRequest<Result<CompliancePolicyEvaluationDto>>;
