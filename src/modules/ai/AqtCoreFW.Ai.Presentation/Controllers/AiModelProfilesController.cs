using AqtCoreFW.Ai.Application.Commands.CreateModelProfile;
using AqtCoreFW.Ai.Application.Commands.UpdateModelProfile;
using AqtCoreFW.Ai.Application.DTOs;
using AqtCoreFW.Ai.Application.Queries.GetModelProfiles;
using AqtCoreFW.Ai.Domain.Enums;
using AqtCoreFW.SharedKernel.Api;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Ai.Presentation.Controllers;

/// <summary>
/// AI model profile management endpoints (admin resource).
/// GET    /api/v1/ai/model-profiles               — list all active profiles
/// POST   /api/v1/ai/model-profiles               — create profile; returns AiModelProfileDto (Id = PublicId Guid)
/// PUT    /api/v1/ai/model-profiles/{publicId}    — update profile config/flags; publicId = T2 Guid AK
/// </summary>
[Route("api/v1/ai/model-profiles")]
[Authorize]
public sealed class AiModelProfilesController(ISender mediator) : ApiControllerBase
{
    /// <summary>List all active AI model profiles.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<AiModelProfileDto>>), 200)]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        ToApiResponse(await mediator.Send(new GetModelProfilesQuery(), ct));

    /// <summary>Create a new AI model profile.</summary>
    [HttpPost]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(typeof(ApiResponse<AiModelProfileDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 422)]
    public async Task<IActionResult> Create(
        [FromBody] CreateModelProfileBody body,
        CancellationToken ct) =>
        ToApiResponse(await mediator.Send(
            new CreateModelProfileCommand(
                body.Name, body.Provider, body.ModelId, body.Endpoint,
                body.MaxTokens, body.Temperature, body.CostPerToken, body.IsDefault),
            ct));

    /// <summary>Update model profile config and/or default/active flags. publicId = T2 Guid AK (external addressing).</summary>
    [HttpPut("{publicId:guid}")]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(typeof(ApiResponse<AiModelProfileDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    [ProducesResponseType(typeof(ApiResponse<object>), 422)]
    public async Task<IActionResult> Update(
        Guid publicId,
        [FromBody] UpdateModelProfileBody body,
        CancellationToken ct) =>
        ToApiResponse(await mediator.Send(
            new UpdateModelProfileCommand(publicId, body.MaxTokens, body.Temperature, body.IsDefault, body.IsActive),
            ct));
}

// ── Request body records ───────────────────────────────────────────────────────

public sealed record CreateModelProfileBody(
    string Name,
    AiProvider Provider,
    string ModelId,
    string? Endpoint,
    int MaxTokens,
    decimal Temperature,
    decimal CostPerToken,
    bool IsDefault = false);

public sealed record UpdateModelProfileBody(
    int? MaxTokens,
    decimal? Temperature,
    bool? IsDefault,
    bool? IsActive);
