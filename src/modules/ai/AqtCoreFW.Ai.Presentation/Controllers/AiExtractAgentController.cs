using AqtCoreFW.SharedKernel.AI;
using AqtCoreFW.SharedKernel.Api;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.SharedKernel.Presentation;
using FluentResults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Ai.Presentation.Controllers;

/// <summary>
/// OCR document extraction, ReAct agent, and content auto-flagging endpoints.
/// POST /api/v1/ai/extract       — extract structured fields from uploaded document image
/// POST /api/v1/ai/agent/execute — execute an autonomous agent task
/// POST /api/v1/ai/flag          — classify content for sensitivity/risk (auto-flagging)
/// </summary>
[Route("api/v1/ai")]
[Authorize]
public sealed class AiExtractAgentController(
    IAiDocumentExtractor extractor,
    IAiAgent agent,
    IAiAutoFlaggingService flaggingService)
    : ApiControllerBase
{
    // ── Document extraction (OCR) ─────────────────────────────────────────────

    /// <summary>
    /// Upload a document image and extract structured fields using Ollama vision.
    /// Supported: image/png, image/jpeg, image/webp.
    /// Returns title, full text content, and key-value field pairs.
    /// </summary>
    [HttpPost("extract")]
    [EnableRateLimiting("write-ops")]
    [RequestSizeLimit(20 * 1024 * 1024)] // 20 MB cap
    [ProducesResponseType(typeof(ApiResponse<ExtractedDocument>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    public async Task<IActionResult> ExtractDocument(
        IFormFile file,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse<object>.Fail(
                [new ValidationError("A file must be uploaded.", "file")]));

        var mimeType = file.ContentType ?? "application/octet-stream";

        byte[] fileBytes;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms, ct);
            fileBytes = ms.ToArray();
        }

        var result = await extractor.ExtractAsync(fileBytes, mimeType, ct);
        return ToApiResponse(result);
    }

    // ── Agent execution ───────────────────────────────────────────────────────

    /// <summary>
    /// Execute an autonomous ReAct agent task.
    /// The agent iterates (max 5) using built-in read-only tools: search_cases,
    /// get_case_detail, classify_text, summarize.
    /// </summary>
    [HttpPost("agent/execute")]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(typeof(ApiResponse<AgentResponse>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    public async Task<IActionResult> ExecuteAgent(
        [FromBody] AgentExecuteRequest body,
        CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null)
            return BadRequest(ApiResponse<object>.Fail(
                [new ValidationError("TenantId is required.", "tenantId")]));

        if (string.IsNullOrWhiteSpace(body.TaskType))
            return BadRequest(ApiResponse<object>.Fail(
                [new ValidationError("TaskType is required.", "taskType")]));

        var task = new AgentTask(
            TaskType: body.TaskType,
            Parameters: body.Parameters ?? new Dictionary<string, string>(),
            TenantId: tenantId.Value);

        var result = await agent.ExecuteAsync(task, ct);
        return ToApiResponse(result);
    }

    // ── Content auto-flagging ─────────────────────────────────────────────────

    /// <summary>
    /// Classify text content for government data sensitivity and risk.
    /// Returns riskLevel (low/medium/high/critical), detectedFlags, and explanation.
    /// Always succeeds — returns low risk when the AI backend is unavailable (fail-open).
    /// </summary>
    [HttpPost("flag")]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(typeof(ApiResponse<AutoFlagResult>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    public async Task<IActionResult> FlagContent(
        [FromBody] FlagContentRequest body,
        CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null)
            return BadRequest(ApiResponse<object>.Fail(
                [new ValidationError("TenantId is required.", "tenantId")]));

        if (string.IsNullOrWhiteSpace(body.Content))
            return BadRequest(ApiResponse<object>.Fail(
                [new ValidationError("Content is required.", "content")]));

        var result = await flaggingService.FlagAsync(body.Content, tenantId.Value, ct);
        return ToApiResponse(Result.Ok(result));
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    // Use new keyword to avoid CS0108 — base class does not define this signature
    private new Guid? ResolveTenantId()
    {
        // Only resolve from JWT claims — X-Tenant-Id header fallback removed (tenant spoofing fix H1-BE-Sec)
        var claim = User.FindFirst("tenant_id")?.Value
                    ?? User.FindFirst("tenantId")?.Value;
        if (Guid.TryParse(claim, out var t)) return t;
        return null;
    }
}

// ── Request DTOs ──────────────────────────────────────────────────────────────

/// <summary>Body for POST /api/v1/ai/agent/execute.</summary>
public sealed record AgentExecuteRequest(
    string TaskType,
    IReadOnlyDictionary<string, string>? Parameters = null);

/// <summary>Body for POST /api/v1/ai/flag.</summary>
public sealed record FlagContentRequest(string Content);
