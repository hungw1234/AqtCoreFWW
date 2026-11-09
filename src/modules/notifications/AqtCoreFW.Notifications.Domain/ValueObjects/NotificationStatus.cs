namespace AqtCoreFW.Notifications.Domain.ValueObjects;

/// <summary>Notification delivery status.</summary>
public enum NotificationStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2,
    Read = 3
}
