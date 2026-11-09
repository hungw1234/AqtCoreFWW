namespace AqtCoreFW.Forms.Application.DTOs;

/// <summary>API projection of a FormTemplateSignerRole entity.</summary>
public sealed record FormTemplateSignerRoleDto(
    long Id,
    string RoleKey,
    string RoleLabelVi,
    int DisplayOrder,
    bool IsRequired,
    string? PlacementJson);
