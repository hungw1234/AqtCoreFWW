using AqtCoreFW.Ai.Application.Commands.IngestDocument;
using AqtCoreFW.Ai.Application.Commands.SendChatMessage;
using AqtCoreFW.Ai.Application.Queries.GetChatHistory;
using AqtCoreFW.SharedKernel.AI;
using AqtCoreFW.SharedKernel.Api;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Ai.Presentation.Controllers;

/// <summary>
/// AI module REST + SSE endpoints.
/// POST /api/v1/ai/chat             — send message, get full response
/// POST /api/v1/ai/chat/stream      — SSE streaming chat (text/event-stream)
/// POST /api/v1/ai/documents/ingest — ingest document text to vector store
/// GET  /api/v1/ai/chat/{sessionId}/history — paginated chat history
/// POST /api/v1/ai/extract          — OCR document extraction (see AiExtractController)
/// POST /api/v1/ai/agent/execute    — ReAct agent execution (see AiExtractController)
/// </summary>
[Route("api/v1/ai")]
[Authorize]
public sealed class AiController(
    ISender mediator,
    ILocalAiStreamingChatService streamingChat)
    : ApiControllerBase
{
    // ── Chat (non-streaming) ─────────────────────────────────────────────────

    /// <summary>Send a message and receive the full AI response.</summary>
    [HttpPost("chat")]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(typeof(ApiResponse<SendChatMessageResponse>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    public async Task<IActionResult> Chat(
        [FromBody] SendChatRequest body,
        CancellationToken ct)
    {
        var (tenantId, userId) = ResolveIdentity();
        if (tenantId is null || userId is null)
            return BadRequest(ApiResponse<object>.Fail(
                [new AqtCoreFW.SharedKernel.Errors.ValidationError("TenantId and UserId are required.", "identity")]));

        var result = await mediator.Send(new SendChatMessageCommand(
            SessionId: body.SessionId,
            Message: body.Message,
            TenantId: tenantId.Value,
            UserId: userId.Value,
            Classification: body.Classification), ct);

        return ToApiResponse(result);
    }

    // ── Chat (SSE streaming) ─────────────────────────────────────────────────

    /// <summary>
    /// SSE streaming chat — yields tokens as Server-Sent Events.
    /// Content-Type: text/event-stream. Each event: "data: {token}\n\n".
    /// Final event carries [DONE] marker.
    /// </summary>
    [HttpPost("chat/stream")]
    [EnableRateLimiting("write-ops")]
    public async Task Stream(
        [FromBody] StreamChatRequest body,
        CancellationToken ct)
    {
        var (tenantId, userId) = ResolveIdentity();
        if (tenantId is null || userId is null)
        {
            Response.StatusCode = 400;
            return;
        }

        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no"; // disable nginx buffering

        var ctx = new AiContext(
            TenantId: tenantId.Value,
            UserId: userId.Value,
            CorrelationId: HttpContext.TraceIdentifier,
            Classification: body.Classification);

        await foreach (var token in streamingChat.StreamAsync(ctx, body.Message, ct))
        {
            if (ct.IsCancellationRequested) break;

            var data = token.IsComplete ? "[DONE]" : token.Token;
            await Response.WriteAsync($"data: {data}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
    }

    // ── Document ingestion ───────────────────────────────────────────────────

    /// <summary>Chunk, embed, and index document text into the tenant vector store.</summary>
    [HttpPost("documents/ingest")]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    public async Task<IActionResult> IngestDocument(
        [FromBody] IngestDocumentRequest body,
        CancellationToken ct)
    {
        var (tenantId, _) = ResolveIdentity();
        if (tenantId is null)
            return BadRequest(ApiResponse<object>.Fail(
                [new AqtCoreFW.SharedKernel.Errors.ValidationError("TenantId is required.", "tenantId")]));

        var result = await mediator.Send(new IngestDocumentCommand(
            FileId: body.FileId,
            Text: body.Text,
            TenantId: tenantId.Value), ct);

        return ToApiResponse(result);
    }

    // ── Chat history ─────────────────────────────────────────────────────────

    /// <summary>Get paginated chat message history for a session. sessionId = T1 long PK of AiSession.</summary>
    [HttpGet("chat/{sessionId:long}/history")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ChatMessageDto>>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> GetHistory(
        long sessionId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var (tenantId, _) = ResolveIdentity();
        if (tenantId is null)
            return BadRequest(ApiResponse<object>.Fail(
                [new AqtCoreFW.SharedKernel.Errors.ValidationError("TenantId is required.", "tenantId")]));

        var result = await mediator.Send(new GetChatHistoryQuery(
            SessionId: sessionId,
            TenantId: tenantId.Value,
            Page: page,
            PageSize: pageSize), ct);

        return ToApiResponse(result);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private (Guid? tenantId, Guid? userId) ResolveIdentity()
    {
        Guid? tenantId = null;
        Guid? userId = null;

        // Only resolve tenant from JWT claims — X-Tenant-Id header fallback removed (tenant spoofing fix H1-BE-Sec)
        var tenantClaim = User.FindFirst("tenant_id")?.Value
                          ?? User.FindFirst("tenantId")?.Value;
        if (Guid.TryParse(tenantClaim, out var t)) tenantId = t;

        var userClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                        ?? User.FindFirst("sub")?.Value;
        if (Guid.TryParse(userClaim, out var u)) userId = u;

        return (tenantId, userId);
    }
}

// ── Request DTOs ──────────────────────────────────────────────────────────────

/// <summary>SessionId = T1 long PK of AiSession (null = start new session).</summary>
public sealed record SendChatRequest(
    string Message,
    long? SessionId = null,
    ClassificationLevel Classification = ClassificationLevel.Internal);

public sealed record StreamChatRequest(
    string Message,
    ClassificationLevel Classification = ClassificationLevel.Internal);

public sealed record IngestDocumentRequest(
    Guid FileId,
    string Text);
