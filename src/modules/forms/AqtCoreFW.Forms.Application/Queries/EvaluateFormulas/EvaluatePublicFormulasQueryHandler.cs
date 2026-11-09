using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Application.Services;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Queries.EvaluateFormulas;

/// <summary>
/// Resolves a PUBLIC template by Code (same two-step pattern as
/// <c>GetPublicSignatureDocHashQueryHandler</c>: Dapper public_forms_bypass id-lookup then EF
/// <c>GetPublicWithFieldsAsync</c> tenant-pin), then batch-evaluates every active Formula field
/// against the caller-supplied draft values via <see cref="FormulaBatchEvaluator"/>. Read-only —
/// never persists, never touches submit-time storage.
/// </summary>
public sealed class EvaluatePublicFormulasQueryHandler(
    IReadDbConnection db,
    IFormTemplateRepository templateRepository,
    IFormulaEvaluationService formulaEvaluationService)
    : IRequestHandler<EvaluatePublicFormulasQuery, Result<FormulaEvaluationResultDto>>
{
    public async Task<Result<FormulaEvaluationResultDto>> Handle(
        EvaluatePublicFormulasQuery request,
        CancellationToken cancellationToken)
    {
        // Step 1: resolve Id + TenantId by Code via Dapper. Anonymous caller has no ambient
        // tenant → resolve on a public_forms_bypass connection so the FormTemplates RLS FILTER
        // carve-out admits the IsPublic=1 row under a non-db_owner login.
        const string idSql = """
            SELECT Id, TenantId FROM forms.FormTemplates
            WHERE Code = @Code AND Status = 'Active' AND IsDeleted = 0 AND IsPublic = 1
            """;

        var row = await db.QueryFirstOrDefaultWithPublicFormsBypassAsync<TemplateIdRow>(
            idSql, new { Code = request.TemplateCode }, cancellationToken);

        if (row is null)
            return Result.Fail(new NotFoundError(
                $"Form '{request.TemplateCode}' không tìm thấy hoặc chưa active."));

        // Step 2: load with fields via EF repository — pins SESSION_CONTEXT to the resolved
        // tenant so the EF read passes the standard FormTemplates RLS FILTER under a
        // non-db_owner login.
        var template = await templateRepository.GetPublicWithFieldsAsync(
            row.Id, row.TenantId, cancellationToken);

        if (template is null)
            return Result.Fail(new NotFoundError(
                $"Form '{request.TemplateCode}' không tìm thấy."));

        var activeFormulaFields = template.Fields
            .Where(f => f.IsActive && f.Type == FormFieldType.Formula)
            .ToList();

        var results = await FormulaBatchEvaluator.EvaluateAllAsync(
            activeFormulaFields, request.Values, formulaEvaluationService, cancellationToken);

        return Result.Ok(new FormulaEvaluationResultDto(results));
    }

    // Dapper projection row — same minimal shape as GetPublicSignatureDocHashQueryHandler.TemplateIdRow.
    // Internal (not private) so unit tests can stub QueryFirstOrDefaultWithPublicFormsBypassAsync<T>
    // against this exact row type, mirroring SubmitPublicFormCommandHandler.TemplateIdRow.
    internal sealed class TemplateIdRow
    {
        public long Id { get; set; }
        public Guid TenantId { get; set; }
    }
}
