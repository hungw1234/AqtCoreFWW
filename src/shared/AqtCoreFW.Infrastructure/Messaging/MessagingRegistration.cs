using AqtCoreFW.SharedKernel.Application;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Infrastructure.Messaging;

/// <summary>
/// DI extension for MassTransit message bus.
/// Call services.AddMessageBus(config) in Program.cs after AddInfrastructure().
/// Transport switch: Bus:Transport = "InMemory" (default/dev) | "RabbitMQ" (staging/prod).
/// </summary>
public static class MessagingRegistration
{
    public static IServiceCollection AddMessageBus(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator>? registerConsumers = null)
    {
        // Bind options
        services.Configure<RabbitMqOptions>(
            configuration.GetSection(RabbitMqOptions.SectionName));

        // MessagingDbContext for dead_letters table.
        // Fail fast if connection string is missing — DeadLetterConsumer needs a real DB at
        // first failure (typically 3am during retry exhaustion), and we must not defer the
        // failure to that path.
        var connStr = configuration.GetConnectionString("Default")
            ?? configuration["Database:ConnectionString"]
            ?? throw new InvalidOperationException(
                "MessagingRegistration: connection string not found. " +
                "Expected ConnectionStrings:Default or Database:ConnectionString.");
        services.AddDbContext<MessagingDbContext>(opts =>
            opts.UseSqlServer(connStr, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", "messaging")));

        // Consumer idempotency ledger (Slice 1C): non-idempotent consumers claim a dedup key before
        // acting so an at-least-once redelivery does not double-effect. Scoped — shares the consume
        // scope's MessagingDbContext.
        services.AddScoped<IProcessedMessageStore, ProcessedMessageStore>();

        // Bounded-growth sweep for the ledger. Idempotent set-based DELETE, so running in more than one
        // host (Api + AuthServer both call AddMessageBus) is benign; stripped in integration tests.
        services.AddHostedService<ProcessedMessageCleanupJob>();

        var transport = configuration.GetValue<string>("Bus:Transport") ?? "InMemory";

        services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();

            // Dead letter consumer — always registered
            x.AddConsumer<DeadLetterConsumer>();

            // Allow modules to register their own consumers
            registerConsumers?.Invoke(x);

            if (transport.Equals("RabbitMQ", StringComparison.OrdinalIgnoreCase))
                ConfigureRabbitMq(x, configuration);
            else
                x.UsingInMemory((ctx, cfg) =>
                {
                    cfg.UseMessageRetry(r =>
                        r.Intervals(
                            TimeSpan.FromSeconds(5),
                            TimeSpan.FromSeconds(30),
                            TimeSpan.FromMinutes(5)));
                    // H-02: Outgoing messages only sent after consumer succeeds (dedup on retry)
                    cfg.UseInMemoryOutbox(ctx);
                    // Slice 1D: pin each consume to the message's tenant (payload-derived) so
                    // background writes set SESSION_CONTEXT / honour tenant filters.
                    cfg.UseConsumeFilter(typeof(TenantScopeConsumeFilter<>), ctx);
                    cfg.ConfigureEndpoints(ctx);
                });
        });

        services.AddScoped<IMessageBus, MassTransitMessageBus>();

        return services;
    }

    private static void ConfigureRabbitMq(
        IBusRegistrationConfigurator x,
        IConfiguration configuration)
    {
        // IsNullOrEmpty, not ??: appsettings ships Username/Password as "" (secrets are never
        // committed) — "" ?? x keeps "", and the bus then depends on MassTransit's own treatment
        // of empty credentials. Explicit fallback keeps the bus and the rabbitmq health probe
        // (HealthCheckConfiguration) on identical credentials by construction.
        static string Or(string? value, string fallback) =>
            string.IsNullOrEmpty(value) ? fallback : value;
        var host = Or(configuration["Bus:RabbitMq:Host"], "localhost");
        var vhost = Or(configuration["Bus:RabbitMq:VHost"], "/");
        var user = Or(configuration["Bus:RabbitMq:Username"], "guest");
        var pass = Or(configuration["Bus:RabbitMq:Password"], "guest");

        x.UsingRabbitMq((ctx, cfg) =>
        {
            cfg.Host(host, vhost, h =>
            {
                h.Username(user);
                h.Password(pass);
            });

            // Exponential backoff — 5s / 30s / 5m (avoids queue overload on transient errors)
            cfg.UseMessageRetry(r =>
                r.Intervals(
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(30),
                    TimeSpan.FromMinutes(5)));

            // Prefetch 16 — balanced throughput without memory pressure
            cfg.PrefetchCount = 16;

            // H-02: Outgoing messages only sent after consumer succeeds (dedup on retry)
            cfg.UseInMemoryOutbox(ctx);

            // System.Text.Json serialization
            cfg.UseRawJsonSerializer();

            // Slice 1D: pin each consume to the message's tenant (payload-derived) so
            // background writes set SESSION_CONTEXT / honour tenant filters.
            cfg.UseConsumeFilter(typeof(TenantScopeConsumeFilter<>), ctx);

            cfg.ConfigureEndpoints(ctx);
        });
    }
}
