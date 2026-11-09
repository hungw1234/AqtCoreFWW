using AqtCoreFW.Ai.Application.Commands.CreatePromptTemplate;
using AqtCoreFW.Ai.Application.Commands.UpdatePromptTemplate;
using AqtCoreFW.Ai.Application.DTOs;
using AqtCoreFW.Ai.Application.Queries.GetPromptTemplates;
using AqtCoreFW.Ai.Domain.Enums;
using AqtCoreFW.SharedKernel.Api;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Ai.Presentation.Controllers;

/// <summary>
/// AI prompt template management endpoints.
/// GET    /api/v1/ai/prompt-templates                       — list active templates (optional ?category=)
/// POST   /api/v1/ai/prompt-templates                       — create template; returns DTO with Guid Id
/// PUT    /api/v1/ai/prompt-templates/{publicId:guid}       — update prompts and/or active flag; publicId = Guid AK
/// </summary>
[Route("api/v1/ai/prompt-templates")]
[Authorize]
public sealed class AiPromptTemplatesController(ISender mediator) : ApiControllerBase
{
    /// <summary>List active prompt templates, optionally filtered by category.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<AiPromptTemplateDto>>), 200)]
    public async Task<IActionResult> GetAll(
        [FromQuery] PromptCategory? category,
        CancellationToken ct) =>
        ToApiResponse(await mediator.Send(new GetPromptTemplatesQuery(category), ct));

    /// <summary>Create a new prompt template at version 1. Returns DTO with Id = PublicId (Guid).</summary>
    [HttpPost]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(typeof(ApiResponse<AiPromptTemplateDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 422)]
    public async Task<IActionResult> Create(
        [FromBody] CreatePromptTemplateBody body,
        CancellationToken ct) =>
        ToApiResponse(await mediator.Send(
            new CreatePromptTemplateCommand(
                body.Name, body.Category, body.SystemPrompt, body.UserPromptTemplate),
            ct));

    /// <summary>Update prompt content (auto-increments version) and/or active flag. publicId = Guid AK (T2 external address).</summary>
    [HttpPut("{publicId:guid}")]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(typeof(ApiResponse<AiPromptTemplateDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    [ProducesResponseType(typeof(ApiResponse<object>), 422)]
    public async Task<IActionResult> Update(
        Guid publicId,
        [FromBody] UpdatePromptTemplateBody body,
        CancellationToken ct) =>
        ToApiResponse(await mediator.Send(
            new UpdatePromptTemplateCommand(publicId, body.SystemPrompt, body.UserPromptTemplate, body.IsActive),
            ct));
}

// ── Request body records ───────────────────────────────────────────────────────

public sealed record CreatePromptTemplateBody(
    string Name,
    PromptCategory Category,
    string SystemPrompt,
    string UserPromptTemplate);

public sealed record UpdatePromptTemplateBody(
    string? SystemPrompt,
    string? UserPromptTemplate,
    bool? IsActive);
