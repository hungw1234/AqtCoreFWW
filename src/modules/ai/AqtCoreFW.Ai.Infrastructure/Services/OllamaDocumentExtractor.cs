using AqtCoreFW.SharedKernel.AI;
using FluentResults;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace AqtCoreFW.Ai.Infrastructure.Services;

/// <summary>
/// OCR document extractor using Ollama's multimodal vision capability.
/// Sends base64-encoded image content to the LLM and parses the JSON response.
/// Supports: image/png, image/jpeg, image/webp.
/// Timeout: 120s (CPU inference limit).
/// Error codes: GOV_AI_002 (timeout/unavailable), GOV_AI_OCR_PARSE (bad response).
/// </summary>
public sealed class OllamaDocumentExtractor(
    IChatClient chatClient,
    ILogger<OllamaDocumentExtractor> logger) : IAiDocumentExtractor
{
    // Supported vision mime types
    private static readonly HashSet<string> SupportedMimeTypes =
    [
        "image/png",
        "image/jpeg",
        "image/jpg",
        "image/webp",
        "application/pdf"
    ];

    private const string ExtractionPrompt =
        "Analyze this document image. Extract: Title, full text content, and structured fields " +
        "(name, date, ID number, address, organization, etc.) as key-value pairs. " +
        "Respond ONLY in valid JSON format with this structure: " +
        "{\"title\": \"...\", \"content\": \"...\", \"fields\": {\"key\": \"value\"}}. " +
        "Do not include any text outside the JSON.";

    public async Task<Result<ExtractedDocument>> ExtractAsync(
        byte[] fileBytes, string mimeType, CancellationToken ct = default)
    {
        if (fileBytes is null || fileBytes.Length == 0)
            return Result.Fail<ExtractedDocument>("GOV_AI_OCR_PARSE: File bytes cannot be empty.");

        if (!SupportedMimeTypes.Contains(mimeType.ToLowerInvariant()))
            return Result.Fail<ExtractedDocument>(
                $"GOV_AI_OCR_PARSE: Unsupported mime type '{mimeType}'. Supported: image/png, image/jpeg, image/webp.");

        // application/pdf — treat as image/jpeg (send raw bytes; Ollama handles common formats)
        var effectiveMime = mimeType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)
            ? "image/jpeg"
            : mimeType;

        try
        {
            logger.LogDebug("OllamaDocumentExtractor: extracting {MimeType}, size={Bytes}B",
                mimeType, fileBytes.Length);

            // Build multimodal message: text prompt + image data
            var textContent = new TextContent(ExtractionPrompt);
            var imageContent = new DataContent(fileBytes, effectiveMime);

            var message = new ChatMessage(ChatRole.User, [textContent, imageContent]);
            var response = await chatClient.GetResponseAsync([message], cancellationToken: ct);
            var rawText = response.Text ?? string.Empty;

            return ParseExtractionResponse(rawText);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("OllamaDocumentExtractor: request timed out for {MimeType}", mimeType);
            return Result.Fail<ExtractedDocument>(
                "GOV_AI_002: Ollama document extraction timed out (120s limit for CPU inference).");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OllamaDocumentExtractor: extraction failed for {MimeType}", mimeType);
            return Result.Fail<ExtractedDocument>($"GOV_AI_002: Ollama unavailable — {ex.Message}");
        }
    }

    /// <summary>
    /// Leniently parse LLM output — JSON may be wrapped in markdown code blocks.
    /// </summary>
    private static Result<ExtractedDocument> ParseExtractionResponse(string rawText)
    {
        var json = ExtractJson(rawText);

        if (string.IsNullOrWhiteSpace(json))
            return Result.Fail<ExtractedDocument>(
                "GOV_AI_OCR_PARSE: LLM returned empty response.");

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var title = root.TryGetProperty("title", out var titleEl)
                ? titleEl.GetString() ?? string.Empty
                : string.Empty;

            var content = root.TryGetProperty("content", out var contentEl)
                ? contentEl.GetString() ?? string.Empty
                : string.Empty;

            var fields = new Dictionary<string, string>();
            if (root.TryGetProperty("fields", out var fieldsEl)
                && fieldsEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in fieldsEl.EnumerateObject())
                    fields[prop.Name] = prop.Value.GetString() ?? string.Empty;
            }

            return Result.Ok(new ExtractedDocument(title, content, fields));
        }
        catch (JsonException ex)
        {
            return Result.Fail<ExtractedDocument>(
                $"GOV_AI_OCR_PARSE: Failed to parse LLM JSON response — {ex.Message}. Raw: {Truncate(rawText, 200)}");
        }
    }

    /// <summary>Strip markdown code fences (```json ... ```) and return bare JSON.</summary>
    private static string ExtractJson(string text)
    {
        var trimmed = text.Trim();

        // Strip ```json ... ``` or ``` ... ``` code blocks
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);

            if (firstNewline > 0 && lastFence > firstNewline)
                trimmed = trimmed[(firstNewline + 1)..lastFence].Trim();
        }

        // Find first { and last } to extract JSON object
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
            return trimmed[start..(end + 1)];

        return trimmed;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "...";
}
