using FluentResults;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AqtCoreFW.Ai.Infrastructure.VectorStore;

/// <summary>
/// Qdrant REST API v1.x implementation.
/// Search degraded mode: Qdrant down → empty results (not exception).
/// Upsert failure → GOV_AI_003 (hard failure for indexing).
/// Scalar quantization + tenant_id payload index enabled on collection creation.
/// score_threshold=0.7, limit=5 (CPU RAM budget — Session 6).
/// </summary>
public sealed class QdrantVectorStore(
    HttpClient http,
    IOptions<QdrantOptions> options,
    ILogger<QdrantVectorStore> logger)
    : IQdrantVectorStore
{
    private readonly QdrantOptions _options = options.Value;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<Result> EnsureCollectionAsync(
        string collection, int vectorSize, CancellationToken ct = default)
    {
        try
        {
            // Check if collection already exists
            var check = await http.GetAsync($"/collections/{collection}", ct);
            if (check.IsSuccessStatusCode)
                return Result.Ok();

            // Create with scalar quantization + cosine distance
            var body = new
            {
                vectors = new { size = vectorSize, distance = "Cosine" },
                quantization_config = new
                {
                    scalar = new { type = "int8", quantile = 0.99, always_ram = false }
                }
            };

            var response = await http.PutAsJsonAsync($"/collections/{collection}", body, JsonOpts, ct);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                return Result.Fail($"GOV_AI_003: Qdrant collection creation failed: {err}");
            }

            // Create payload index on tenant_id for efficient partition filtering
            var indexBody = new { field_name = "tenant_id", field_schema = "keyword" };
            await http.PutAsJsonAsync(
                $"/collections/{collection}/index", indexBody, JsonOpts, ct);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Qdrant EnsureCollection failed for {Collection}", collection);
            return Result.Fail($"GOV_AI_003: {ex.Message}");
        }
    }

    public async Task<Result> UpsertAsync(
        string collection, string id, float[] vector,
        Dictionary<string, object> payload, CancellationToken ct = default)
    {
        try
        {
            var body = new
            {
                points = new[]
                {
                    new { id = ToUuid(id), vector, payload }
                }
            };

            var response = await http.PutAsJsonAsync(
                $"/collections/{collection}/points", body, JsonOpts, ct);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                logger.LogError("Qdrant upsert failed for {Collection}/{Id}: {Error}", collection, id, err);
                return Result.Fail($"GOV_AI_003: Qdrant upsert failed — {err}");
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Qdrant upsert exception for {Collection}/{Id}", collection, id);
            return Result.Fail($"GOV_AI_003: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        string collection, float[] queryVector, int topK, float scoreThreshold,
        CancellationToken ct = default)
    {
        try
        {
            var body = new
            {
                vector = queryVector,
                limit = topK,
                score_threshold = scoreThreshold,
                with_payload = true
            };

            var response = await http.PostAsJsonAsync(
                $"/collections/{collection}/points/search", body, JsonOpts, ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Qdrant search degraded for {Collection} — returning empty results", collection);
                return [];
            }

            var result = await response.Content.ReadFromJsonAsync<QdrantSearchResponse>(JsonOpts, ct);
            return result?.Result?.Select(r => new VectorSearchResult(
                Id: r.Id?.ToString() ?? string.Empty,
                Score: r.Score,
                Text: r.Payload.TryGetValue("text", out var t) ? t?.ToString() ?? string.Empty : string.Empty,
                Payload: r.Payload.ToDictionary(k => k.Key, v => v.Value?.ToString() ?? string.Empty)))
                .ToList() ?? [];
        }
        catch (Exception ex)
        {
            // Degraded mode — Qdrant down → empty results, not exception
            logger.LogWarning(ex, "Qdrant search unavailable for {Collection} — degraded mode", collection);
            return [];
        }
    }

    public async Task<Result> DeleteAsync(
        string collection, string id, CancellationToken ct = default)
    {
        try
        {
            var body = new { points = new[] { ToUuid(id) } };
            var response = await http.PostAsJsonAsync(
                $"/collections/{collection}/points/delete", body, JsonOpts, ct);
            return response.IsSuccessStatusCode
                ? Result.Ok()
                : Result.Fail($"GOV_AI_003: Qdrant delete failed for {id}");
        }
        catch (Exception ex)
        {
            return Result.Fail($"GOV_AI_003: {ex.Message}");
        }
    }

    // Qdrant requires UUID-format point IDs — derive deterministically from string key
    private static string ToUuid(string id)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(id);
        var hash = System.Security.Cryptography.MD5.HashData(bytes);
        return new Guid(hash).ToString();
    }

    // Response DTOs
    private sealed record QdrantSearchResponse(
        [property: JsonPropertyName("result")] List<QdrantScoredPoint>? Result);

    private sealed record QdrantScoredPoint(
        [property: JsonPropertyName("id")] object Id,
        [property: JsonPropertyName("score")] float Score,
        [property: JsonPropertyName("payload")] Dictionary<string, object?> Payload);
}
