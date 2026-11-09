using AqtCoreFW.SharedKernel.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AqtCoreFW.Ai.Infrastructure.Services;

/// <summary>
/// Classifies AI-generated text for government data sensitivity using Ollama.
/// Sends content with a structured classification system prompt and parses JSON response.
/// Timeout: 30s (classification is lightweight — no vision, no RAG).
/// Fail-open: returns low risk with empty flags when Ollama is unavailable.
/// </summary>
public sealed class OllamaAutoFlaggingService(
    IChatClient chatClient,
    ILogger<OllamaAutoFlaggingService> logger) : IAiAutoFlaggingService
{
    private static readonly AutoFlagResult LowRiskFallback =
        new("low", [], "Auto-flagging unavailable — defaulting to low risk.");

    // Classification system prompt — instructs LLM to return strict JSON only
    private const string SystemPrompt =
        "You are a government data classification engine. " +
        "Classify the following text for sensitivity and risk. " +
        "Respond ONLY in valid JSON with this exact structure: " +
        "{\"riskLevel\": \"low|medium|high|critical\", " +
        "\"detectedFlags\": [\"pii\"|\"sensitive_data\"|\"policy_violation\"|\"financial\"|\"legal\"], " +
        "\"explanation\": \"brief reason\"}. " +
        "Flag meanings: pii=personal identifiable data, sensitive_data=government secrets or classified info, " +
        "policy_violation=inappropriate or harmful content, financial=monetary or budget data, " +
        "legal=legal obligations or liability. " +
        "Do not include any text outside the JSON.";

    public async Task<AutoFlagResult> FlagAsync(
        string content, Guid tenantId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content))
            return new AutoFlagResult("low", [], "No content to classify.");

        // 30-second timeout — fast classification model, no vision
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            logger.LogDebug(
                "OllamaAutoFlaggingService: classifying content length={Length} tenant={TenantId}",
                content.Length, tenantId);

            var messages = new[]
            {
                new ChatMessage(ChatRole.System, SystemPrompt),
                new ChatMessage(ChatRole.User, content)
            };

            var response = await chatClient.GetResponseAsync(messages, cancellationToken: cts.Token);
            var rawText = response.Text ?? string.Empty;

            return ParseFlagResponse(rawText);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Our internal 30s timeout — not the caller's cancellation
            logger.LogWarning(
                "OllamaAutoFlaggingService: classification timed out for tenant={TenantId}", tenantId);
            return LowRiskFallback;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "OllamaAutoFlaggingService: Ollama unavailable for tenant={TenantId} — returning low risk",
                tenantId);
            return LowRiskFallback;
        }
    }

    /// <summary>
    /// Leniently parse LLM output — JSON may be wrapped in markdown code fences.
    /// Falls back to low risk if JSON is malformed.
    /// </summary>
    private AutoFlagResult ParseFlagResponse(string rawText)
    {
        var json = ExtractJson(rawText);

        if (string.IsNullOrWhiteSpace(json))
        {
            logger.LogWarning("OllamaAutoFlaggingService: empty LLM response — returning low risk");
            return LowRiskFallback;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var riskLevel = root.TryGetProperty("riskLevel", out var rlEl)
                ? rlEl.GetString() ?? "low"
                : "low";

            // Normalise to known values; unknown → low
            if (!KnownRiskLevels.Contains(riskLevel))
                riskLevel = "low";

            var flags = Array.Empty<string>();
            if (root.TryGetProperty("detectedFlags", out var flagsEl)
                && flagsEl.ValueKind == JsonValueKind.Array)
            {
                flags = flagsEl.EnumerateArray()
                    .Select(e => e.GetString() ?? string.Empty)
                    .Where(f => !string.IsNullOrWhiteSpace(f))
                    .ToArray();
            }

            var explanation = root.TryGetProperty("explanation", out var expEl)
                ? expEl.GetString() ?? string.Empty
                : string.Empty;

            return new AutoFlagResult(riskLevel, flags, explanation);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(
                "OllamaAutoFlaggingService: failed to parse JSON — {Message}. Raw: {Raw}",
                ex.Message, Truncate(rawText, 200));
            return LowRiskFallback;
        }
    }

    /// <summary>Strip markdown code fences and return the innermost JSON object.</summary>
    private static string ExtractJson(string text)
    {
        var trimmed = text.Trim();

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline > 0 && lastFence > firstNewline)
                trimmed = trimmed[(firstNewline + 1)..lastFence].Trim();
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
            return trimmed[start..(end + 1)];

        return trimmed;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "...";

    private static readonly HashSet<string> KnownRiskLevels =
        ["low", "medium", "high", "critical"];
}
