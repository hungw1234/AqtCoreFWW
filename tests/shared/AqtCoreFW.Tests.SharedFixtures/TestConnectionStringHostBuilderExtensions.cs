using Microsoft.AspNetCore.Hosting;

namespace AqtCoreFW.Tests.SharedFixtures;

/// <summary>
/// Injects a test connection string into the host's <c>ConfigurationManager</c> phase WITHOUT
/// mutating a process-global environment variable.
///
/// Why this exists: the API/AuthServer <c>Program.cs</c> reads <c>ConnectionStrings:Default</c>
/// (and <c>:Hangfire</c>) at DbContext / Hangfire-storage registration time — the
/// ConfigurationManager phase, before <c>app.Build()</c>. That phase sees only environment
/// variables and command-line args, NOT a <see cref="WebApplicationFactory{TEntryPoint}"/>'s
/// later <c>ConfigureAppConfiguration.AddInMemoryCollection</c> (which is replayed onto the host
/// builder after construction). Fixtures therefore historically hoisted the connection string
/// into a process-global env var — but that env var is a single shared slot, so overlapping
/// fixture lifecycles raced it and hosts booted against the wrong (or a disposed) container.
///
/// <c>UseSetting</c> avoids the global entirely: WebApplicationFactory's DeferredHostBuilder
/// captures host-configuration settings immediately and, at Build(), converts each into a
/// <c>--key=value</c> command-line arg passed to the real entry point. <c>CreateBuilder(args)</c>
/// ingests those as the highest-precedence configuration source, visible at the exact
/// registration phase above. No shared mutable state → no race, and per-fixture isolation is
/// preserved. Precedent in this repo: <c>ApiFactory</c> uses <c>UseEnvironment("Testing")</c>
/// (the same UseSetting→args seam) and Program.cs's environment checks see it.
/// </summary>
public static class TestConnectionStringHostBuilderExtensions
{
    /// <summary>
    /// Sets <c>ConnectionStrings:Default</c> (and optionally <c>:Hangfire</c>) as host settings so
    /// they reach the ConfigurationManager phase. Call at the top of <c>ConfigureWebHost</c>.
    /// Use the <c>:</c> key form (not <c>__</c>); the command-line source splits at the first
    /// <c>=</c>, so embedded <c>;</c>/<c>=</c>/spaces in the connection string are preserved.
    /// </summary>
    public static IWebHostBuilder UseTestConnectionStrings(
        this IWebHostBuilder builder,
        string connectionString,
        bool includeHangfire = false)
    {
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        if (includeHangfire)
            builder.UseSetting("ConnectionStrings:Hangfire", connectionString);
        return builder;
    }
}
