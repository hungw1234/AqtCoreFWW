using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AqtCoreFW.Notifications.Application.Providers;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Notifications.Infrastructure.Sms;

/// <summary>ISmsProvider implementation via HTTP webhook POST (Bearer auth).</summary>
public sealed class WebhookSmsProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<SmsWebhookOptions> options,
    ILogger<WebhookSmsProvider> logger) : ISmsProvider
{
    private readonly SmsWebhookOptions _opts = options.Value;

    public async Task SendAsync(string phoneNumber, string message, CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient("SmsWebhook");
        var payload = JsonSerializer.Serialize(new { to = phoneNumber, text = message });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var maskedNumber = PiiLogMasking.MaskPhone(phoneNumber);

        try
        {
            var response = await client.PostAsync(_opts.WebhookUrl, content, cancellationToken);
            response.EnsureSuccessStatusCode();
            logger.LogInformation("SMS sent to {PhoneNumber}", maskedNumber);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send SMS to {PhoneNumber}", maskedNumber);
            throw;
        }
    }
}

/// <summary>SMS webhook configuration — credentials from Vault in production.</summary>
public sealed class SmsWebhookOptions
{
    public const string SectionName = "SmsWebhook";
    public string WebhookUrl { get; set; } = string.Empty;
    public string BearerToken { get; set; } = string.Empty;
}
