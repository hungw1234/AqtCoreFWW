namespace AqtCoreFW.Rules.Application.DTOs;

/// <summary>Full RuleSet detail including all rules — returned by GetRuleSet.</summary>
public sealed record RuleSetDto(
    Guid Id,
    Guid TenantId,
    string Name,
    string? Description,
    string? Category,
    int Version,
    string Status,
    string? TargetEntityType,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<RuleDto> Rules);

/// <summary>Individual rule within a RuleSet.</summary>
public sealed record RuleDto(
    Guid Id,
    Guid RuleSetId,
    string Name,
    string Expression,
    int Priority,
    bool Enabled,
    string? ErrorMessage,
    string FailureAction);

/// <summary>
/// Decision table detail. Storage stores raw JSON strings; handlers parse on read so the wire format
/// matches the FE consumer shape (typed arrays + dictionary rows). FE renders directly without re-parsing.
/// T1 entity: Id is long internal PK; RuleSetId is parent RuleSet's Guid PublicId.
/// </summary>
public sealed record DecisionTableDto(
    long Id,
    Guid RuleSetId,
    Guid TenantId,
    string Name,
    IReadOnlyList<string> InputColumns,
    IReadOnlyList<string> OutputColumns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>Lightweight RuleSet row for list pages — Dapper requires class with parameterless ctor.</summary>
public sealed class RuleSetListItemDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Version { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Result returned after EvaluateRules or SimulateRules.</summary>
public sealed record RuleEvaluationResultDto(
    bool IsSuccess,
    IReadOnlyList<RuleResultItemDto> Results,
    long DurationMs,
    string? ExplainTrace);

/// <summary>Per-rule outcome within an evaluation result.</summary>
public sealed record RuleResultItemDto(
    string RuleName,
    bool IsSuccess,
    string? ExceptionMessage);
