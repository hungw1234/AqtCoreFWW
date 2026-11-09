using AqtCoreFW.Reporting.Domain.Entities;
using AqtCoreFW.Reporting.Domain.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Reporting.Application.Commands.CreateDashboard;

/// <summary>
/// Creates a new DashboardLayout for the given owner within a tenant.
/// If IsDefault is requested, caller should ensure prior defaults are cleared (handled by service layer or migration).
/// </summary>
public sealed class CreateDashboardCommandHandler(IDashboardLayoutRepository repository)
    : IRequestHandler<CreateDashboardCommand, Result<long>>
{
    public async Task<Result<long>> Handle(
        CreateDashboardCommand request,
        CancellationToken cancellationToken)
    {
        var layout = DashboardLayout.Create(
            request.Name,
            request.OwnerId,
            request.TenantId,
            request.IsDefault);

        await repository.AddAsync(layout, cancellationToken);

        return Result.Ok(layout.Id);
    }
}
