using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Application.Services;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Queries.EvaluateFormulas;

/// <summary>
/// Loads the template (same eager-fields path <c>SubmitFormCommandHandler</c> uses), then
/// batch-evaluates every active Formula field against the caller-supplied draft values via
/// <see cref="FormulaBatchEvaluator"/>. Read-only — never persists, never touches submit-time
/// storage.
/// </summary>
public sealed class EvaluateFormulasQueryHandler(
    IFormTemplateRepository templateRepository,
    IFormulaEvaluationService formulaEvaluationService)
    : IRequestHandler<EvaluateFormulasQuery, Result<FormulaEvaluationResultDto>>
{
    public async Task<Result<FormulaEvaluationResultDto>> Handle(
        EvaluateFormulasQuery request,
        CancellationToken cancellationToken)
    {
        var template = await templateRepository.GetByPublicIdWithFieldsAsync(
            request.TemplatePublicId, request.TenantId, cancellationToken);

        if (template is null || template.Status != FormTemplateStatus.Active)
            return Result.Fail(new NotFoundError(
                $"Form template '{request.TemplatePublicId}' không tìm thấy hoặc chưa active."));

        var activeFormulaFields = template.Fields
            .Where(f => f.IsActive && f.Type == FormFieldType.Formula)
            .ToList();

        var results = await FormulaBatchEvaluator.EvaluateAllAsync(
            activeFormulaFields, request.Values, formulaEvaluationService, cancellationToken);

        return Result.Ok(new FormulaEvaluationResultDto(results));
    }
}
