using AqtCoreFW.Forms.Application.Commands.CreateViewDefinition;
using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Commands.UpdateViewDefinition;

public sealed class UpdateViewDefinitionCommandHandler(IViewDefinitionRepository repository)
    : IRequestHandler<UpdateViewDefinitionCommand, Result<ViewDefinitionDto>>
{
    public async Task<Result<ViewDefinitionDto>> Handle(
        UpdateViewDefinitionCommand request,
        CancellationToken cancellationToken)
    {
        var view = await repository.GetByIdWithDetailsAsync(
            request.Id, request.TenantId, cancellationToken);

        if (view is null)
            return Result.Fail(new NotFoundError($"ViewDefinition {request.Id} không tìm thấy."));

        try
        {
            view.UpdateInfo(
                request.Name,
                request.DefaultSortField,
                request.DefaultSortDir,
                request.PageSize,
                request.IsDefault,
                request.UpdatedBy);

            // Replace columns when provided
            if (request.Columns is not null)
            {
                var newColumns = request.Columns.Select(c =>
                    ViewColumn.Create(
                        view.Id,
                        c.FieldName, c.Label, c.DisplayOrder,
                        c.Sortable, c.Filterable, c.Width, c.Formatter,
                        request.UpdatedBy));
                view.ReplaceColumns(newColumns);
            }

            await repository.UpdateAsync(view, cancellationToken);
            return Result.Ok(CreateViewDefinitionCommandHandler.MapToDto(view));
        }
        catch (ArgumentException ex)
        {
            return Result.Fail(new ValidationError(ex.Message, ex.ParamName ?? ""));
        }
    }
}
