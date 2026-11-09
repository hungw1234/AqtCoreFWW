namespace AqtCoreFW.Infrastructure.Messaging;

/// <summary>
/// Strongly-typed options for RabbitMQ connection.
/// Bound from appsettings: "Bus:RabbitMq" section.
/// Credentials sourced from Vault in staging/prod — never hardcode passwords.
/// </summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "Bus:RabbitMq";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string Username { get; set; } = "guest";

    /// <summary>Populated from Vault at runtime in staging/prod. Dev: appsettings.</summary>
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";

    /// <summary>RabbitMQ management HTTP API port — used by DLQ dashboard.</summary>
    public int ManagementPort { get; set; } = 15672;
}
