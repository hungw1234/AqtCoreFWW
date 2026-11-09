using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Commands.CreateViewDefinition;

public sealed class CreateViewDefinitionCommandHandler(IViewDefinitionRepository repository)
    : IRequestHandler<CreateViewDefinitionCommand, Result<ViewDefinitionDto>>
{
    public async Task<Result<ViewDefinitionDto>> Handle(
        CreateViewDefinitionCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            var view = ViewDefinition.Create(
                request.Name,
                request.EntityType,
                request.Type,
                request.TenantId,
                request.CreatedBy,
                request.DefaultSortField,
                request.DefaultSortDir,
                request.PageSize,
                request.IsDefault);

            // Attach columns
            if (request.Columns is not null)
            {
                foreach (var col in request.Columns)
                {
                    var column = ViewColumn.Create(
                        view.Id,
                        col.FieldName,
                        col.Label,
                        col.DisplayOrder,
                        col.Sortable,
                        col.Filterable,
                        col.Width,
                        col.Formatter,
                        request.CreatedBy);
                    view.AddColumn(column);
                }
            }

            // Attach filters
            if (request.Filters is not null)
            {
                foreach (var f in request.Filters)
                {
                    var filter = ViewFilter.Create(
                        view.Id,
                        f.FieldName,
                        f.Operator,
                        f.DefaultValue,
                        f.IsRequired,
                        request.CreatedBy);
                    view.AddFilter(filter);
                }
            }

            await repository.AddAsync(view, cancellationToken);
            return Result.Ok(MapToDto(view));
        }
        catch (ArgumentException ex)
        {
            return Result.Fail(new SharedKernel.Errors.ValidationError(ex.Message, ex.ParamName ?? ""));
        }
    }

    internal static ViewDefinitionDto MapToDto(ViewDefinition v) =>
        new(
            v.Id,
            v.TenantId,
            v.Name,
            v.EntityType,
            v.Type.ToString(),
            v.DefaultSortField,
            v.DefaultSortDir,
            v.PageSize,
            v.IsDefault,
            v.CreatedAt,
            v.Columns.Select(c => new ViewColumnDto(
                c.Id, c.ViewDefinitionId, c.FieldName, c.Label,
                c.Width, c.Sortable, c.Filterable, c.DisplayOrder,
                c.Formatter.ToString())).ToList(),
            v.Filters.Select(f => new ViewFilterDto(
                f.Id, f.ViewDefinitionId, f.FieldName,
                f.Operator.ToString(), f.DefaultValue, f.IsRequired)).ToList());
}
