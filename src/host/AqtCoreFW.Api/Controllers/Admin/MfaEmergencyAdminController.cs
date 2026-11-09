using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Identity.Application.MfaEmergency;
using AqtCoreFW.SharedKernel.Api;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Api.Controllers.Admin;

/// <summary>
/// 2-admin approval flow for emergency MFA disable (QÄ742 1.6).
/// Request: first admin submits reason. Approve/Reject: second admin actions.
/// Approver must differ from requester â€” enforced in handler, not controller.
/// </summary>
[Route("api/v1/admin/security/mfa-emergency-disable")]
[RequiresMfa]
[EnableRateLimiting("write-ops")]
public sealed class MfaEmergencyAdminController(ISender mediator) : ApiControllerBase
{
    /// <summary>
    /// POST /api/v1/admin/security/mfa-emergency-disable/requests
    /// Submit a request to disable MFA for a target user.
    /// Requires Security.MfaEmergency.Request permission.
    /// Returns 201 Created with the request ID.
    /// </summary>
    [HttpPost("requests")]
    [Authorize(Policy = PermCodes.Security.MfaEmergency.Request)]
    [ProducesResponseType(typeof(ApiResponse<long>), 201)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    [ProducesResponseType(typeof(ApiResponse<object>), 409)]
    public async Task<IActionResult> SubmitRequest(
        [FromBody] EmergencyDisableRequestBody body,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(
            new RequestEmergencyDisableCommand(body.TargetUserId, body.Reason), ct);

        if (result.IsFailed)
            return ToApiResponse(result);

        return StatusCode(201, ApiResponse<long>.Ok(result.Value));
    }

    /// <summary>
    /// POST /api/v1/admin/security/mfa-emergency-disable/requests/{requestId}/approve
    /// Second admin approves or rejects the pending request.
    /// Requires Security.MfaEmergency.Approve permission.
    /// Approver must be different from the original requester (enforced in handler).
    /// </summary>
    [HttpPost("requests/{requestId:long}/approve")]
    [Authorize(Policy = PermCodes.Security.MfaEmergency.Approve)]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ApiResponse<object>), 403)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> Approve(
        long requestId,
        [FromBody] EmergencyDisableApprovalBody body,
        CancellationToken ct = default)
        => ToApiResponse(await mediator.Send(
            new ApproveEmergencyDisableCommand(requestId, body.Approve, body.RejectionNote), ct));
}

// â”€â”€ Request DTOs â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

public sealed record EmergencyDisableRequestBody(Guid TargetUserId, string Reason);
public sealed record EmergencyDisableApprovalBody(bool Approve, string? RejectionNote = null);
