using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.SystemParams.Domain.Entities;

public enum AnnouncementSeverity { Info, Warning, Critical }

/// <summary>
/// System-wide announcement shown to users. Cached 1min on public endpoint.
/// T1: long PK (DB-assigned, via non-generic AuditableEntity). No PublicId — not externally API-routed by Guid.
/// </summary>
public sealed class SystemAnnouncement : AuditableEntity
{
    public string Title { get; private set; } = default!;
    public string Content { get; private set; } = default!;
    public AnnouncementSeverity Severity { get; private set; }
    public DateTimeOffset? StartAt { get; private set; }
    public DateTimeOffset? EndAt { get; private set; }
    public bool IsActive { get; private set; }
    public string? TargetRoles { get; private set; }

    private SystemAnnouncement() { }

    public static SystemAnnouncement Create(
        string title, string content, AnnouncementSeverity severity,
        DateTimeOffset? startAt = null, DateTimeOffset? endAt = null,
        string? targetRoles = null) =>
        new()
        {
            Title = title,
            Content = content,
            Severity = severity,
            StartAt = startAt,
            EndAt = endAt,
            IsActive = true,
            TargetRoles = targetRoles
        };

    public void Update(string title, string content, AnnouncementSeverity severity,
        DateTimeOffset? startAt, DateTimeOffset? endAt, bool isActive)
    {
        Title = title;
        Content = content;
        Severity = severity;
        StartAt = startAt;
        EndAt = endAt;
        IsActive = isActive;
        MarkUpdated();
    }

    public void Deactivate() { IsActive = false; MarkUpdated(); }
}
