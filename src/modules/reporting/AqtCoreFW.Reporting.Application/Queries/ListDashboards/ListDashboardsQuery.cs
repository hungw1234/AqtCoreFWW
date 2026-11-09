using AqtCoreFW.Reporting.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Reporting.Application.Queries.ListDashboards;

public record ListDashboardsQuery(Guid OwnerId, Guid TenantId) : IRequest<Result<IReadOnlyList<DashboardLayoutDto>>>;
