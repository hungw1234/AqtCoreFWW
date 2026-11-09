using AqtCoreFW.Audit.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Audit.Application.Queries.GetAuditStatistics;

/// <summary>Returns aggregated audit counts by day/week/month, by action, by module.</summary>
public sealed record GetAuditStatisticsQuery(Guid? TenantId) : IRequest<Result<AuditStatisticsDto>>;
