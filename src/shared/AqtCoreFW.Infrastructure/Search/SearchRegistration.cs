using AqtCoreFW.SharedKernel.Application.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Infrastructure.Search;

/// <summary>
/// DI registration for search infrastructure.
/// Reads "Search:Provider" to select the active adapter:
///   "SqlServer"      → SqlServerFtsSearchService (default, zero infra overhead)
///   "Elasticsearch"  → ElasticsearchSearchService (requires ES/OpenSearch node)
/// </summary>
public static class SearchRegistration
{
    public static IServiceCollection AddSearchInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<SearchOptions>(configuration.GetSection(SearchOptions.SectionName));

        var provider = configuration.GetValue<string>("Search:Provider") ?? "SqlServer";

        if (string.Equals(provider, "Elasticsearch", StringComparison.OrdinalIgnoreCase))
        {
            // Named HttpClient with base address resolved at runtime from options
            services.AddHttpClient<ElasticsearchSearchService>((sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<SearchOptions>>().Value;
                client.BaseAddress = new Uri(opts.Elasticsearch.Url);

                // Basic auth (Vault-provided credentials in prod)
                if (!string.IsNullOrEmpty(opts.Elasticsearch.Username))
                {
                    var credentials = Convert.ToBase64String(
                        System.Text.Encoding.UTF8.GetBytes(
                            $"{opts.Elasticsearch.Username}:{opts.Elasticsearch.Password}"));
                    client.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
                }
            });

            services.AddScoped<ISearchService, ElasticsearchSearchService>();
        }
        else
        {
            // Default: SQL Server FTS — no extra infra required
            services.AddScoped<ISearchService, SqlServerFtsSearchService>();
        }

        return services;
    }
}
