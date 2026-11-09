using AqtCoreFW.Reporting.Application.Abstractions;
using AqtCoreFW.Reporting.Domain.Errors;
using AqtCoreFW.Reporting.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Reporting.Application.Commands.UpdateWidget;

/// <summary>
/// Applies partial updates to a ReportWidget.
/// If DataQuery is updated, re-validates SQL security before persisting.
/// </summary>
public sealed class UpdateWidgetCommandHandler(IReportWidgetRepository repository)
    : IRequestHandler<UpdateWidgetCommand, Result>
{
    public async Task<Result> Handle(
        UpdateWidgetCommand request,
        CancellationToken cancellationToken)
    {
        var widget = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (widget is null)
            return Result.Fail(new NotFoundError($"ReportWidget '{request.Id}' not found."));

        if (request.DataQuery is not null)
        {
            if (!SqlValidationHelper.StartsWithSelect(request.DataQuery))
                return Result.Fail(ReportingErrors.DangerousSql("SQL must start with SELECT"));

            var dangerousKeyword = SqlValidationHelper.FindDangerousKeyword(request.DataQuery);
            if (dangerousKeyword is not null)
                return Result.Fail(ReportingErrors.DangerousSql(dangerousKeyword));

            widget.UpdateQuery(request.DataQuery);
        }

        if (request.Config is not null)
            widget.UpdateConfig(request.Config);

        if (request.Name is not null || request.RefreshSeconds.HasValue)
            widget.UpdateDetails(request.Name, request.RefreshSeconds);

        await repository.SaveChangesAsync(cancellationToken);

        return Result.Ok();
    }
}
