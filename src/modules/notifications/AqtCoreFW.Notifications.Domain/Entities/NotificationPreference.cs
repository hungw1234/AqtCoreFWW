using AqtCoreFW.Notifications.Domain.ValueObjects;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Notifications.Domain.Entities;

/// <summary>
/// Per-user notification channel preferences.
///
/// T1 entity: AuditableEntity (long Id).
/// TenantId/UserId: Guid (FK exception list — tenant isolation + identity ref).
/// </summary>
public sealed class NotificationPreference : AuditableEntity, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public NotificationChannel Channel { get; private set; } = default!;
    public bool IsEnabled { get; private set; } = true;

    private NotificationPreference() { } // EF Core

    public static NotificationPreference Create(
        Guid tenantId,
        Guid userId,
        NotificationChannel channel,
        bool isEnabled,
        Guid createdBy)
    {
        var pref = new NotificationPreference
        {
            TenantId = tenantId,
            UserId = userId,
            Channel = channel,
            IsEnabled = isEnabled
        };
        pref.SetAuditCreate(createdBy);
        return pref;
    }

    public void SetEnabled(bool isEnabled, Guid modifiedBy)
    {
        IsEnabled = isEnabled;
        SetAuditUpdate(modifiedBy);
    }
}
