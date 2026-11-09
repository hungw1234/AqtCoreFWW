namespace AqtCoreFW.Infrastructure.Search;

/// <summary>
/// Search infrastructure configuration. Bound from "Search" config section.
/// Provider = "SqlServer" (default) | "Elasticsearch"
/// </summary>
public sealed class SearchOptions
{
    public const string SectionName = "Search";

    /// <summary>"SqlServer" or "Elasticsearch"</summary>
    public string Provider { get; set; } = "SqlServer";

    public ElasticsearchSearchOptions Elasticsearch { get; set; } = new();
}

/// <summary>Elasticsearch/OpenSearch connection options.</summary>
public sealed class ElasticsearchSearchOptions
{
    /// <summary>Node URL. E.g. "http://localhost:9200"</summary>
    public string Url { get; set; } = "http://localhost:9200";

    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Index naming strategy: "prefix" = {indexName}-{tenantId}, "field" = tenant_id field filter.
    /// Default is "field" (simpler ops, fewer indices).
    /// </summary>
    public string TenantIsolation { get; set; } = "field";
}
