using System.Text.Json.Serialization;

namespace AqtCoreFW.SharedKernel.Api;

/// <summary>
/// RFC 9457 Problem Details + GOV extensions.
/// ErrorCode follows GOV_MODULE_NNN convention (e.g. GOV_CAS_001).
/// DetailVi provides Vietnamese localisation for citizen-facing errors.
/// <para>
/// Metadata: optional structured payload surfaced via DEFAULT-DENY OPT-IN.
/// Only errors that explicitly set the reserved "PublicMetadata" key in FluentResults metadata
/// will populate this field. All other internal keys are stripped at the Fail() boundary.
/// Omitted from JSON when null (additive, backward-compatible).
/// </para>
/// </summary>
public sealed record ApiError(
    string Code,
    string Message,
    string? DetailVi = null,
    string? TraceId = null,
    string? Property = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<string, object?>? Metadata = null);
