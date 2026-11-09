using AqtCoreFW.Forms.Domain.Errors;
using AqtCoreFW.Forms.Domain.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Commands.BulkReorderFields;

/// <summary>
/// Atomically updates display order for all fields in a template.
/// Updates all entities in-memory first, then calls SaveChanges once
/// so the operation is truly atomic (all-or-nothing within one transaction).
/// TemplatePublicId resolved to internal long Id via IFormTemplateRepository before field lookup.
/// </summary>
public sealed class BulkReorderFieldsCommandHandler(
    IFormFieldRepository fieldRepository,
    IFormTemplateRepository templateRepository)
    : IRequestHandler<BulkReorderFieldsCommand, Result>
{
    public async Task<Result> Handle(BulkReorderFieldsCommand request, CancellationToken ct)
    {
        // Resolve public Guid AK → internal long Id for field lookup
        var template = await templateRepository.GetByPublicIdAsync(
            request.TemplatePublicId, request.TenantId, ct);
        if (template is null)
            return Result.Fail(FormErrors.TemplateNotFound(request.TemplatePublicId));

        var fields = await fieldRepository.GetByTemplateIdAsync(template.Id, activeOnly: true, ct);

        // Validate all submitted field IDs belong to this template
        var fieldMap = fields.ToDictionary(f => f.Id);
        foreach (var item in request.Items)
        {
            if (!fieldMap.ContainsKey(item.FieldId))
                return Result.Fail(FormErrors.FieldNotFound(item.FieldId));
        }

        // Validate no duplicate order values
        var orders = request.Items.Select(i => i.NewOrder).ToList();
        if (orders.Distinct().Count() != orders.Count)
            return Result.Fail("Duplicate order values are not allowed");

        // Apply new order to each field in-memory (no SaveChanges yet)
        foreach (var item in request.Items)
        {
            fieldMap[item.FieldId].UpdateDisplayOrder(item.NewOrder);
        }

        // Single SaveChanges — atomic: all fields update or none
        // Call UpdateAsync on last item to trigger SaveChanges for the batch
        // EF Core change tracker already tracks all modified entities
        if (request.Items.Count > 0)
        {
            var lastItem = request.Items[^1];
            await fieldRepository.UpdateAsync(fieldMap[lastItem.FieldId], ct);
        }

        return Result.Ok();
    }
}
