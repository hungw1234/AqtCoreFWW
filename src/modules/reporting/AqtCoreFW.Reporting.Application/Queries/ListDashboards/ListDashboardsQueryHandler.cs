using AqtCoreFW.Reporting.Application.DTOs;
using AqtCoreFW.Reporting.Domain.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Reporting.Application.Queries.ListDashboards;

public sealed class ListDashboardsQueryHandler(IDashboardLayoutRepository repository)
    : IRequestHandler<ListDashboardsQuery, Result<IReadOnlyList<DashboardLayoutDto>>>
{
    public async Task<Result<IReadOnlyList<DashboardLayoutDto>>> Handle(
        ListDashboardsQuery request,
        CancellationToken cancellationToken)
    {
        var layouts = await repository.GetByOwnerAsync(request.OwnerId, request.TenantId, cancellationToken);

        var dtos = layouts
            .Select(l => new DashboardLayoutDto(
                l.Id, l.Name, l.OwnerId, l.IsDefault,
                l.WidgetPositions, l.TenantId, l.CreatedAt))
            .ToList();

        return Result.Ok<IReadOnlyList<DashboardLayoutDto>>(dtos);
    }
}
