using AqtCoreFW.Reporting.Application.DTOs;
using AqtCoreFW.Reporting.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Reporting.Application.Queries.GetDashboard;

public sealed class GetDashboardQueryHandler(IDashboardLayoutRepository repository)
    : IRequestHandler<GetDashboardQuery, Result<DashboardLayoutDto>>
{
    public async Task<Result<DashboardLayoutDto>> Handle(
        GetDashboardQuery request,
        CancellationToken cancellationToken)
    {
        var layout = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (layout is null)
            return Result.Fail(new NotFoundError($"DashboardLayout '{request.Id}' not found."));

        // T1 entity: layout.Id is long (internal PK, no PublicId)
        return Result.Ok(new DashboardLayoutDto(
            layout.Id,
            layout.Name,
            layout.OwnerId,
            layout.IsDefault,
            layout.WidgetPositions,
            layout.TenantId,
            layout.CreatedAt));
    }
}
