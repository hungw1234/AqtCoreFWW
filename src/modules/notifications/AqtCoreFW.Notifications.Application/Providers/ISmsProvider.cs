namespace AqtCoreFW.Notifications.Application.Providers;

/// <summary>
/// SMS delivery interface — webhook-based provider by default.
/// Clients implement their own provider (Twilio, VIETTEL, etc).
/// </summary>
public interface ISmsProvider
{
    Task SendAsync(string phoneNumber, string message, CancellationToken cancellationToken = default);
}
