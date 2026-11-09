using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.BackgroundJobs.Integration.Tests.Fixtures;

/// <summary>
/// Shared fixture for background job integration tests.
/// Uses Hangfire.MemoryStorage — no SQL container or Docker required.
/// Creates a BackgroundJobServer with a single worker so jobs execute within the test process.
/// All tests in [Collection("HangfireIntegration")] share one server instance.
/// </summary>
public sealed class HangfireFixture : IAsyncLifetime
{
    private BackgroundJobServer _server = null!;

    /// <summary>Root DI container — used to resolve job class instances via ServiceProviderJobActivator.</summary>
    public IServiceProvider Services { get; private set; } = null!;

    public IBackgroundJobClient JobClient { get; private set; } = null!;
    public IRecurringJobManager RecurringJobManager { get; private set; } = null!;

    public Task InitializeAsync()
    {
        // Configure Hangfire global config to use InMemory storage (process-scoped for tests)
        GlobalConfiguration.Configuration
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseMemoryStorage();

        // Build minimal DI container for job class resolution
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));

        // Register test job classes — Hangfire resolves these via ServiceProviderJobActivator
        services.AddTransient<TestJob>();
        services.AddTransient<TestScheduledJob>();
        services.AddTransient<TestRecurringJob>();

        Services = services.BuildServiceProvider();

        // BackgroundJobClient and RecurringJobManager use GlobalConfiguration storage
        JobClient = new BackgroundJobClient();
        RecurringJobManager = new RecurringJobManager();

        // Start a single-worker server — polls every 100ms so tests don't wait long
        var serverOptions = new BackgroundJobServerOptions
        {
            WorkerCount = 1,
            Queues = ["default"],
            SchedulePollingInterval = TimeSpan.FromMilliseconds(100),
            ShutdownTimeout = TimeSpan.FromSeconds(10),
            Activator = new ServiceProviderJobActivator(Services),
        };

        _server = new BackgroundJobServer(serverOptions);

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _server.SendStop();
        _server.Dispose();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Hangfire job activator that resolves job instances from the test DI container.
/// Creates a new DI scope per job execution so transient/scoped services are lifecycle-correct.
/// </summary>
public sealed class ServiceProviderJobActivator(IServiceProvider serviceProvider) : JobActivator
{
    public override object ActivateJob(Type jobType) =>
        serviceProvider.GetRequiredService(jobType);

    public override JobActivatorScope BeginScope(JobActivatorContext context) =>
        new ServiceScopeJobActivatorScope(serviceProvider.CreateScope());
}

/// <summary>Wraps an IServiceScope as a Hangfire JobActivatorScope.</summary>
public sealed class ServiceScopeJobActivatorScope(IServiceScope scope) : JobActivatorScope
{
    public override object Resolve(Type type) =>
        scope.ServiceProvider.GetRequiredService(type);

    public override void DisposeScope() => scope.Dispose();
}

[CollectionDefinition(CollectionName)]
public sealed class HangfireCollection : ICollectionFixture<HangfireFixture>
{
    public const string CollectionName = "HangfireIntegration";
}
