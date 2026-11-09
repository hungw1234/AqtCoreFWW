using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Infrastructure.Messaging;
using AqtCoreFW.SharedKernel.Api;
using AqtCoreFW.SharedKernel.Presentation;
using FluentResults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Api.Controllers.Admin;

/// <summary>
/// Admin API for dead letter queue management.
/// Allows inspecting, retrying, and quarantining failed messages.
/// Requires SystemAdmin role.
/// </summary>
[Route("api/v1/admin/dead-letters")]
[Authorize(Policy = PermCodes.System.Config.Manage)]
[RequiresMfa]
[EnableRateLimiting("write-ops")]
public sealed class DeadLettersAdminController(DeadLetterService service) : ApiControllerBase
{
    /// <summary>List dead letters with optional filters.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<DeadLetterDto>>), 200)]
    public async Task<IActionResult> List(
        [FromQuery] DeadLetterStatus? status,
        [FromQuery] string? messageType,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var items = await service.ListAsync(status, messageType, from, to, page, pageSize, ct);
        var dtos = items.Select(DeadLetterDto.From).ToList();
        return Ok(ApiResponse<List<DeadLetterDto>>.Ok(dtos));
    }

    /// <summary>Get full dead letter payload by ID.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<DeadLetterDto>), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var letter = await service.GetByIdAsync(id, ct);
        if (letter is null)
            return NotFound(ApiResponse<DeadLetterDto>.Fail([new Error($"Dead letter {id} not found.")]));
        return Ok(ApiResponse<DeadLetterDto>.Ok(DeadLetterDto.From(letter)));
    }

    /// <summary>Requeue a Pending dead letter back to the bus.</summary>
    [HttpPost("{id:guid}/retry")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Retry(Guid id, CancellationToken ct)
    {
        var ok = await service.RetryAsync(id, ct);
        return ok ? NoContent() : NotFound(ApiResponse<object>.Fail([new Error($"Dead letter {id} not found or not in Pending status.")]));
    }

    /// <summary>Quarantine a dead letter â€” prevent accidental retry.</summary>
    [HttpPost("{id:guid}/quarantine")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Quarantine(
        Guid id,
        [FromBody] QuarantineRequest request,
        CancellationToken ct)
    {
        var ok = await service.QuarantineAsync(id, request.Reason, ct);
        return ok ? NoContent() : NotFound(ApiResponse<object>.Fail([new Error($"Dead letter {id} not found or not in Pending status.")]));
    }
}

/// <summary>DTO for dead letter list/detail responses.</summary>
public sealed record DeadLetterDto(
    Guid Id,
    string MessageType,
    string Payload,
    string FailureReason,
    string OriginalQueue,
    DateTimeOffset ReceivedAt,
    string Status,
    DateTimeOffset? RetriedAt,
    string? QuarantineReason)
{
    public static DeadLetterDto From(MessageDeadLetter m) => new(
        m.Id, m.MessageType, m.Payload, m.FailureReason, m.OriginalQueue,
        m.ReceivedAt, m.Status.ToString(), m.RetriedAt, m.QuarantineReason);
}

/// <summary>Request body for quarantine action.</summary>
public sealed record QuarantineRequest(string Reason);
