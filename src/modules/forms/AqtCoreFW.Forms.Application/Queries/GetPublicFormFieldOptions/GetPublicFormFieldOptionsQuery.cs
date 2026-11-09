using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Forms.Application.Queries.GetPublicFormFieldOptions;

/// <summary>
/// C1 — anonymous public cascade. Returns the selectable options for one field of a PUBLIC form,
/// addressed by the form's business <paramref name="Code"/> plus the field's stable
/// <paramref name="FieldKey"/> (NOT the internal bigint PK — the anonymous surface must not leak it).
/// Tenant is server-resolved from the form's Code (never from a caller-supplied value), mirroring
/// <c>EvaluatePublicFormulasQueryHandler</c>. <paramref name="ParentValues"/> is the cascade parents'
/// selected values keyed by filterParamName (null ⇒ unfiltered), identical semantics to the authed
/// <c>GetFormFieldOptionsQuery</c>.
/// </summary>
public sealed record GetPublicFormFieldOptionsQuery(
    string Code,
    string FieldKey,
    IReadOnlyDictionary<string, string>? ParentValues = null)
    : IQuery<IReadOnlyList<DataSourceOptionDto>>;
