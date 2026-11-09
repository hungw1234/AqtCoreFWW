using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Forms.Application.Queries.GetFormFieldOptions;

/// <summary>
/// Returns the list of selectable options for a reference or dropdown field. FieldId = long (T1 internal PK).
/// Cascade v1: <paramref name="ParentValues"/> is OPTIONAL (default null ⇒ old unfiltered behavior) — the
/// selected values of this field's cascade parents keyed by filterParamName, used to filter the option-set.
/// </summary>
// arch:accept-long T1 FormField: child of T2 FormTemplate aggregate, addressed via parent PublicId
public sealed record GetFormFieldOptionsQuery(
    long FieldId,
    Guid TenantId,
    IReadOnlyDictionary<string, string>? ParentValues = null)
    : IQuery<IReadOnlyList<DataSourceOptionDto>>;
