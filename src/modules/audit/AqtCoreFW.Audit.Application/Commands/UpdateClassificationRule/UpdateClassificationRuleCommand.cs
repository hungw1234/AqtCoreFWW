using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Audit.Application.Commands.UpdateClassificationRule;

/// <summary>
/// Updates handling rules for a single data classification level.
/// RowVersion (base64) is required for optimistic concurrency.
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record UpdateClassificationRuleCommand(
    DataClassificationLevel Level,
    bool MustEncryptAtRest,
    bool AllowExport,
    int? MaxRetentionDays,
    string RowVersion) : ICommand;
