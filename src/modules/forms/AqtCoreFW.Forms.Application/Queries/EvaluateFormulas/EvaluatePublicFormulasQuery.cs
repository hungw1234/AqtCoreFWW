using AqtCoreFW.Forms.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Queries.EvaluateFormulas;

/// <summary>
/// Batch-evaluates every active Formula field of a PUBLIC template against caller-supplied
/// draft values. Anonymous, rate-limited — resolves the template by Code only; TenantId is
/// NEVER accepted from the client, mirroring <see cref="GetSignatureDocHash.GetPublicSignatureDocHashQuery"/>.
/// </summary>
/// <param name="TemplateCode">Public form code (slug) — same identifier used by <c>GET /api/v1/public/forms/{code}</c>.</param>
/// <param name="Values">Draft form values keyed by FieldKey (same shape as a submit payload).</param>
public sealed record EvaluatePublicFormulasQuery(
    string TemplateCode,
    IDictionary<string, object?> Values) : IRequest<Result<FormulaEvaluationResultDto>>;
