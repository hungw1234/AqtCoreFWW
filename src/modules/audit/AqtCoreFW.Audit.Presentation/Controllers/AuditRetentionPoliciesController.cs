using AqtCoreFW.Audit.Application.Commands.CreateAuditRetentionPolicy;
using AqtCoreFW.Audit.Application.Commands.DeleteAuditRetentionPolicy;
using AqtCoreFW.Audit.Application.Commands.UpdateAuditRetentionPolicy;
using AqtCoreFW.Audit.Application.DTOs;
using AqtCoreFW.Audit.Application.Queries.GetAuditRetentionPolicies;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AqtCoreFW.Audit.Presentation.Controllers;

/// <summary>
/// QÄ 742 Â§3.2.b â€” admin CRUD for audit log retention policies. Per-tenant + per-category
/// rules with global-default fallback. Phase-05 ships the FE; this is the BE-only slice.
/// Tenant scoping mirrors <see cref="AuditLogsController"/> â€” non-SystemAdmin callers see
/// only their tenant's rows + global defaults.
/// </summary>
[Route("api/v1/audit/retention-policies")]
[Authorize(Policy = PermCodes.Audit.Retention.View)]
public sealed class AuditRetentionPoliciesController(ISender mediator, ITenantContext tenantContext)
    : ApiControllerBase
{
    /// <summary>List policies visible to the caller (own tenant + globals; SystemAdmin sees everything).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AuditRetentionPolicyDto>), 200)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        ToApiResponse(await mediator.Send(
            new GetAuditRetentionPoliciesQuery(tenantContext.IsSystemAdmin ? null : tenantContext.TenantId),
            cancellationToken));

    /// <summary>Create a new (tenant, category) retention rule.</summary>
    [HttpPost]
    [Authorize(Policy = PermCodes.Audit.Retention.Manage)]
    [ProducesResponseType(typeof(Guid), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> Create(
        [FromBody] CreateAuditRetentionPolicyRequest body,
        CancellationToken cancellationToken)
    {
        // Non-SystemAdmin can only create policies for their own tenant.
        var effectiveTenantId = tenantContext.IsSystemAdmin
            ? body.TenantId
            : tenantContext.TenantId;

        var cmd = new CreateAuditRetentionPolicyCommand(
            effectiveTenantId,
            body.Category,
            body.RetentionDays,
            body.ArchiveBeforePurge,
            body.ArchivePath);

        return ToApiResponse(await mediator.Send(cmd, cancellationToken));
    }

    /// <summary>Update retention window + archive flags (RowVersion concurrency token required).</summary>
    [HttpPut("{id:long}")]
    [Authorize(Policy = PermCodes.Audit.Retention.Manage)]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Update(
        long id,
        [FromBody] UpdateAuditRetentionPolicyRequest body,
        CancellationToken cancellationToken) =>
        ToApiResponse(await mediator.Send(
            new UpdateAuditRetentionPolicyCommand(
                id,
                body.RetentionDays,
                body.ArchiveBeforePurge,
                body.ArchivePath,
                body.RowVersion),
            cancellationToken));

    /// <summary>Delete a non-default policy (the seeded global-default ID is protected).</summary>
    [HttpDelete("{id:long}")]
    [Authorize(Policy = PermCodes.Audit.Retention.Manage)]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Delete(
        long id,
        [FromQuery] string rowVersion,
        CancellationToken cancellationToken) =>
        ToApiResponse(await mediator.Send(
            new DeleteAuditRetentionPolicyCommand(id, rowVersion),
            cancellationToken));
}

public sealed record CreateAuditRetentionPolicyRequest(
    Guid? TenantId,
    AuditCategory? Category,
    int RetentionDays,
    bool ArchiveBeforePurge,
    string? ArchivePath);

public sealed record UpdateAuditRetentionPolicyRequest(
    int RetentionDays,
    bool ArchiveBeforePurge,
    string? ArchivePath,
    string RowVersion);
