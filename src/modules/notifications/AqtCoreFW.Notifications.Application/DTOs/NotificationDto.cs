namespace AqtCoreFW.Notifications.Application.DTOs;

// Class with init-only setters — Dapper materialization friendly.
// Positional record + nullable + DateTime/DateTimeOffset coercion fails (Dapper requires
// parameterless ctor or exact column-type match for positional members).
public sealed class NotificationDto
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid RecipientUserId { get; init; }
    public string Subject { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
    public bool IsRead { get; init; }
    public DateTimeOffset? ReadAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
