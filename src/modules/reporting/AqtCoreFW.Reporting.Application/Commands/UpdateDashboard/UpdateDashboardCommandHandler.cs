using AqtCoreFW.Reporting.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Reporting.Application.Commands.UpdateDashboard;

/// <summary>Applies partial updates to an existing DashboardLayout.</summary>
public sealed class UpdateDashboardCommandHandler(IDashboardLayoutRepository repository)
    : IRequestHandler<UpdateDashboardCommand, Result>
{
    public async Task<Result> Handle(
        UpdateDashboardCommand request,
        CancellationToken cancellationToken)
    {
        var layout = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (layout is null)
            return Result.Fail(new NotFoundError($"DashboardLayout '{request.Id}' not found."));

        if (request.Name is not null)
            layout.Rename(request.Name);

        if (request.WidgetPositions is not null)
            layout.UpdateLayout(request.WidgetPositions);

        if (request.IsDefault is true)
            layout.SetAsDefault();

        await repository.SaveChangesAsync(cancellationToken);

        return Result.Ok();
    }
}
