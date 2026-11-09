namespace AqtCoreFW.Notifications.Application.DTOs;

/// <summary>
/// T2 entity DTO — Id is NotificationTemplate.PublicId (Guid AK), not the internal long PK.
/// NotificationTemplate retiered T1→T2 per Phase-07 (IPublicIdentified + Guid PublicId AK).
/// </summary>
public sealed record NotificationTemplateDto(
    Guid Id,
    Guid TenantId,
    string TemplateKey,
    string SubjectTemplate,
    string BodyTemplate,
    string Channel,
    bool IsDefault,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
