using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Queries.GetFormFieldOptions;

/// <summary>
/// Resolves options for a FormField:
///   - Static Options list (Dropdown/MultiSelect) → mapped directly.
///   - EnumRef/InternalRef/ExternalRef → delegated to IDataSourceResolverFactory.
///   - All other types → empty list.
/// </summary>
public sealed class GetFormFieldOptionsQueryHandler(
    IFormFieldRepository fieldRepository,
    IDataSourceResolverFactory resolverFactory)
    : IRequestHandler<GetFormFieldOptionsQuery, Result<IReadOnlyList<DataSourceOptionDto>>>
{
    private static readonly HashSet<FormFieldType> ReferenceTypes =
    [
        FormFieldType.EnumRef,
        FormFieldType.InternalRef,
        FormFieldType.ExternalRef
    ];

    public async Task<Result<IReadOnlyList<DataSourceOptionDto>>> Handle(
        GetFormFieldOptionsQuery request,
        CancellationToken cancellationToken)
    {
        var fieldResult = await fieldRepository.GetByIdAsync(request.FieldId, cancellationToken);
        if (fieldResult.IsFailed)
            return Result.Fail(fieldResult.Errors);

        var field = fieldResult.Value;

        // Static options (Dropdown, MultiSelect, etc.)
        if (field.Options.Count > 0)
        {
            var staticOptions = field.Options
                .OrderBy(o => o.DisplayOrder)
                .Select(o => new DataSourceOptionDto(o.Value, o.LabelEn, o.LabelVi))
                .ToList();
            return Result.Ok<IReadOnlyList<DataSourceOptionDto>>(staticOptions);
        }

        // Reference types resolved via data source infrastructure
        if (ReferenceTypes.Contains(field.Type) && !string.IsNullOrWhiteSpace(field.DataSourceJson))
        {
            var resolver = resolverFactory.Create(field.Type);
            var resolved = await resolver.ResolveAsync(
                field.DataSourceJson, request.TenantId, request.ParentValues, cancellationToken);
            return Result.Ok(resolved);
        }

        return Result.Ok<IReadOnlyList<DataSourceOptionDto>>([]);
    }
}
