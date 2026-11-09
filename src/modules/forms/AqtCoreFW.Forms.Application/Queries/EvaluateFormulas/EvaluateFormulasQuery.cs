using AqtCoreFW.Forms.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Queries.EvaluateFormulas;

/// <summary>
/// Batch-evaluates every active Formula field of a template against caller-supplied draft
/// values. Read-only, tenant-scoped realtime compute for authenticated form fill —
/// reuses the same sandboxed <c>IFormulaEvaluationService</c> submit-time uses.
/// </summary>
/// <param name="TemplatePublicId">T2 Guid AK for the template.</param>
/// <param name="TenantId">Caller's tenant — resolved server-side, never from the client.</param>
/// <param name="Values">Draft form values keyed by FieldKey (same shape as a submit payload).</param>
public sealed record EvaluateFormulasQuery(
    Guid TemplatePublicId,
    Guid TenantId,
    IDictionary<string, object?> Values) : IRequest<Result<FormulaEvaluationResultDto>>;
