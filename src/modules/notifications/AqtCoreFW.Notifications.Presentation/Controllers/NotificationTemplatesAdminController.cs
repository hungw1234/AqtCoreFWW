using AqtCoreFW.SharedKernel.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using AqtCoreFW.SharedKernel.Presentation;
using AqtCoreFW.Notifications.Application.Commands.CreateNotificationTemplate;
using AqtCoreFW.Notifications.Application.Commands.UpdateNotificationTemplate;
using AqtCoreFW.Notifications.Application.Commands.DeleteNotificationTemplate;
using AqtCoreFW.Notifications.Application.Queries.GetNotificationTemplates;
using AqtCoreFW.Notifications.Application.Queries.GetNotificationTemplateById;

namespace AqtCoreFW.Notifications.Presentation.Controllers;

/// <summary>
/// Admin API for managing notification templates.
/// Requires SystemAdmin or TenantAdmin role.
/// NotificationTemplate is T2 (Guid PublicId AK) — routes use {publicId:guid}.
/// </summary>
[Authorize(Policy = PermCodes.Notif.Template.Manage)]
[Route("api/v1/admin/notification-templates")]
[RequiresMfa]
[EnableRateLimiting("write-ops")]
public sealed class NotificationTemplatesAdminController(ISender mediator) : ApiControllerBase
{
    /// <summary>List notification templates for a tenant.</summary>
    [HttpGet]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetTemplates(
        [FromQuery] Guid tenantId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? channel = null,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        return ToApiResponse(await mediator.Send(
            new GetNotificationTemplatesQuery(tenantId, page, pageSize, channel), cancellationToken));
    }

    /// <summary>Get a single notification template by Guid PublicId (T2 entity external AK).</summary>
    [HttpGet("{publicId:guid}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetTemplateById(
        Guid publicId,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(new GetNotificationTemplateByIdQuery(publicId), cancellationToken));

    /// <summary>Create a new notification template. Returns the Guid PublicId of the created template.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(Guid), 200)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> CreateTemplate(
        [FromBody] CreateNotificationTemplateCommand command,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(command, cancellationToken));

    /// <summary>Update subject and body of an existing notification template.</summary>
    [HttpPut("{publicId:guid}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> UpdateTemplate(
        Guid publicId,
        [FromBody] UpdateNotificationTemplateRequest body,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new UpdateNotificationTemplateCommand(publicId, body.SubjectTemplate, body.BodyTemplate),
            cancellationToken));

    /// <summary>Soft-delete a notification template.</summary>
    [HttpDelete("{publicId:guid}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeleteTemplate(
        Guid publicId,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(new DeleteNotificationTemplateCommand(publicId), cancellationToken));
}

/// <summary>Request body for PUT — only mutable fields.</summary>
public sealed record UpdateNotificationTemplateRequest(string SubjectTemplate, string BodyTemplate);
