using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.ExportInstanceAudit;

/// <summary>
/// Export a workflow instance's full audit trail in JSON, CSV, or PDF format.
/// Tenant-scoped — cross-tenant requests return NotFound.
/// </summary>
public sealed record ExportInstanceAuditQuery(
    Guid InstanceId,
    Guid TenantId,
    AuditExportFormat Format) : IRequest<Result<AuditExportResult>>;
