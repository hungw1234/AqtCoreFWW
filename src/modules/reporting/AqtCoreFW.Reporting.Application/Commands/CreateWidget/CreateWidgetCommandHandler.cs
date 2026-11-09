using AqtCoreFW.Reporting.Application.Abstractions;
using AqtCoreFW.Reporting.Domain.Entities;
using AqtCoreFW.Reporting.Domain.Errors;
using AqtCoreFW.Reporting.Domain.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Reporting.Application.Commands.CreateWidget;

/// <summary>
/// Creates a ReportWidget.
/// SECURITY: validates DataQuery must start with SELECT, no dangerous DDL/DML, no SQL comments.
/// TenantId injected server-side — not trusted from external callers.
/// </summary>
public sealed class CreateWidgetCommandHandler(IReportWidgetRepository repository)
    : IRequestHandler<CreateWidgetCommand, Result<long>>
{
    public async Task<Result<long>> Handle(
        CreateWidgetCommand request,
        CancellationToken cancellationToken)
    {
        if (!SqlValidationHelper.StartsWithSelect(request.DataQuery))
            return Result.Fail(ReportingErrors.DangerousSql("SQL must start with SELECT"));

        var dangerousKeyword = SqlValidationHelper.FindDangerousKeyword(request.DataQuery);
        if (dangerousKeyword is not null)
            return Result.Fail(ReportingErrors.DangerousSql(dangerousKeyword));

        var widget = ReportWidget.Create(
            request.Name,
            request.Type,
            request.DataQuery,
            request.TenantId,
            request.CreatedBy,
            request.Config,
            request.RefreshSeconds);

        await repository.AddAsync(widget, cancellationToken);

        return Result.Ok(widget.Id);
    }
}
