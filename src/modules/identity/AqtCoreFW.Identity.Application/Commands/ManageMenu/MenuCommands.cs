using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Identity.Application.Commands.ManageMenu;

/// <summary>Create a new menu item.</summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record CreateMenuCommand(
    Guid? ParentId,
    string Code,
    string Title,
    string? Icon,
    string? Route,
    int SortOrder,
    IReadOnlyList<string> PermissionCodes,
    Guid? TenantId) : ICommand<Guid>;

/// <summary>Update an existing menu item.</summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record UpdateMenuCommand(
    Guid Id,
    Guid? ParentId,
    string Title,
    string? Icon,
    string? Route,
    int SortOrder,
    bool IsActive,
    IReadOnlyList<string> PermissionCodes) : ICommand;
