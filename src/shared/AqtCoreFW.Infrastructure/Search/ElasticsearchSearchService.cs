using System.Text;
using System.Text.Json;
using AqtCoreFW.SharedKernel.Application.Search;
using FluentResults;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Infrastructure.Search;

/// <summary>
/// ISearchService backed by Elasticsearch / OpenSearch via raw HTTP.
/// Uses HttpClient instead of the heavy Elastic.Clients.Elasticsearch SDK to avoid
/// a mandatory NuGet dependency when running in SqlServer mode.
/// Tenant isolation: every query includes a filter on tenant_id field.
/// Cursor: search_after array encoded as base64 JSON.
/// Switch to this adapter via: Search:Provider = "Elasticsearch"
/// Query building and response parsing delegated to ElasticQueryBuilder.
/// </summary>
public sealed class ElasticsearchSearchService : ISearchService
{
    private readonly HttpClient _http;
    private readonly ElasticsearchSearchOptions _opts;
    private readonly ILogger<ElasticsearchSearchService> _logger;

    public ElasticsearchSearchService(
        HttpClient http,
        IOptions<SearchOptions> options,
        ILogger<ElasticsearchSearchService> logger)
    {
        _http = http;
        _opts = options.Value.Elasticsearch;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<SearchResult<T>>> SearchAsync<T>(
        SearchRequest request,
        CancellationToken ct = default)
        where T : SearchDocument
    {
        if (string.IsNullOrWhiteSpace(request.IndexName))
            return Result.Fail<SearchResult<T>>("SearchRequest.IndexName is required.");

        if (request.TenantId == Guid.Empty)
            return Result.Fail<SearchResult<T>>("SearchRequest.TenantId is required.");

        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        try
        {
            var indexName = ResolveIndexName(request);
            var body = ElasticQueryBuilder.BuildSearchBody(request, pageSize);
            var json = JsonSerializer.Serialize(body);

            var url = $"{_opts.Url.TrimEnd('/')}/{indexName}/_search";
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(url, content, ct);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Elasticsearch search failed {Status}: {Error}", response.StatusCode, err);
                return Result.Fail<SearchResult<T>>($"Elasticsearch returned {(int)response.StatusCode}");
            }

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            return ElasticQueryBuilder.ParseSearchResponse<T>(responseJson, pageSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Elasticsearch search failed for index {Index}", request.IndexName);
            return Result.Fail<SearchResult<T>>($"Elasticsearch search failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<Result> IndexAsync<T>(T document, CancellationToken ct = default)
        where T : SearchDocument
    {
        try
        {
            var indexName = ResolveIndexNameForDocument(document);
            var url = $"{_opts.Url.TrimEnd('/')}/{indexName}/_doc/{Uri.EscapeDataString(document.Id)}";
            var json = JsonSerializer.Serialize(document);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _http.PutAsync(url, content, ct);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Elasticsearch index failed {Status}: {Error}", response.StatusCode, err);
                return Result.Fail($"Elasticsearch index returned {(int)response.StatusCode}");
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Elasticsearch IndexAsync failed for document {Id}", document.Id);
            return Result.Fail($"Elasticsearch index failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<Result> RemoveAsync(string indexName, string documentId, CancellationToken ct = default)
    {
        try
        {
            var url = $"{_opts.Url.TrimEnd('/')}/{indexName}/_doc/{Uri.EscapeDataString(documentId)}";
            using var response = await _http.DeleteAsync(url, ct);

            // 404 is fine — idempotent
            if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Elasticsearch delete failed {Status}: {Error}", response.StatusCode, err);
                return Result.Fail($"Elasticsearch delete returned {(int)response.StatusCode}");
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Elasticsearch RemoveAsync failed for {Index}/{Id}", indexName, documentId);
            return Result.Fail($"Elasticsearch remove failed: {ex.Message}");
        }
    }

    // --- Index name resolution ---

    private string ResolveIndexName(SearchRequest request)
    {
        // "field" strategy: single index per name, tenant filtered by query
        // "prefix" strategy: separate index per tenant for stronger isolation
        return _opts.TenantIsolation == "prefix"
            ? $"{request.IndexName}-{request.TenantId:N}"
            : request.IndexName;
    }

    private string ResolveIndexNameForDocument<T>(T document) where T : SearchDocument
    {
        // Derive index name from type name (lowercase) when using prefix isolation
        var baseIndex = typeof(T).Name.ToLowerInvariant().Replace("document", "");
        return _opts.TenantIsolation == "prefix"
            ? $"{baseIndex}-{document.TenantId:N}"
            : baseIndex;
    }
}
