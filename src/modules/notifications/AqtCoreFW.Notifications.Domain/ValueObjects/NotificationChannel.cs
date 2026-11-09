using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Notifications.Domain.ValueObjects;

/// <summary>Notification delivery channel value object.</summary>
public sealed class NotificationChannel : ValueObject
{
    public static readonly NotificationChannel Email = new("email");
    public static readonly NotificationChannel Sms = new("sms");
    public static readonly NotificationChannel InApp = new("inapp");

    public string Value { get; }

    private NotificationChannel(string value) => Value = value;

    public static NotificationChannel From(string value) => value.ToLowerInvariant() switch
    {
        "email" => Email,
        "sms" => Sms,
        "inapp" => InApp,
        _ => throw new ArgumentException($"Invalid notification channel: {value}", nameof(value))
    };

    protected override IEnumerable<object?> GetEqualityComponents() { yield return Value; }

    public override string ToString() => Value;
}
