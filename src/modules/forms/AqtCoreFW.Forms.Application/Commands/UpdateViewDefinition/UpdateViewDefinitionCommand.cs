using AqtCoreFW.Forms.Application.Commands.CreateViewDefinition;
using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Forms.Application.Commands.UpdateViewDefinition;

/// <summary>
/// Updates mutable view-level settings. All optional fields are null-means-no-change.
/// When Columns is provided, replaces all existing columns with the new set.
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record UpdateViewDefinitionCommand(
    long Id,
    Guid TenantId,
    Guid UpdatedBy,
    string? Name = null,
    string? DefaultSortField = null,
    string? DefaultSortDir = null,
    int? PageSize = null,
    bool? IsDefault = null,
    List<CreateViewColumnDto>? Columns = null) : ICommand<ViewDefinitionDto>;
