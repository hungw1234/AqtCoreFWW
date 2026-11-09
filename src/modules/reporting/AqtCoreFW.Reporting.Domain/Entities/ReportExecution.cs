using AqtCoreFW.Reporting.Domain.Enums;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Reporting.Domain.Entities;

/// <summary>
/// Tracks a single execution of a ReportDefinition.
/// Created by RunReportCommandHandler, updated by ExecuteReportJob (Hangfire).
///
/// T2 entity: AuditableEntity + IPublicIdentified (PublicId = v7 monotonic Guid AK).
/// ReportDefinitionId: long (FK to T2 ReportDefinition.Id internal PK — changed from Guid).
/// RequestedBy: Guid (FK exception list — identity ref to AspNetUsers.Id).
/// </summary>
public sealed class ReportExecution : AuditableEntity, IPublicIdentified
{
    public Guid PublicId { get; private set; }
    /// <summary>FK to ReportDefinition.Id (long, T2 internal PK).</summary>
    public long ReportDefinitionId { get; private set; }

    /// <summary>JSON-serialized parameter values provided at run time.</summary>
    public string Parameters { get; private set; } = "{}";

    /// <summary>Requester identity ref — Guid (FK exception list — AspNetUsers.Id).</summary>
    public Guid RequestedBy { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public ExecutionStatus Status { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Relative path to generated file (within reports temp storage).</summary>
    public string? ResultFilePath { get; private set; }

    public string? ErrorMessage { get; private set; }

    private ReportExecution() { }

    public static ReportExecution Create(
        IGuidGenerator gen,
        long reportDefinitionId,
        string parameters,
        Guid requestedBy)
    {
        return new ReportExecution
        {
            PublicId = gen.NewIdMonotonic(),
            ReportDefinitionId = reportDefinitionId,
            Parameters = parameters,
            RequestedBy = requestedBy,
            RequestedAt = DateTimeOffset.UtcNow,
            Status = ExecutionStatus.Queued
        };
    }

    public void MarkRunning()
    {
        Status = ExecutionStatus.Running;
        StartedAt = DateTimeOffset.UtcNow;
    }

    public void MarkDone(string resultFilePath)
    {
        Status = ExecutionStatus.Done;
        ResultFilePath = resultFilePath;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed(string errorMessage)
    {
        Status = ExecutionStatus.Failed;
        ErrorMessage = errorMessage;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void ClearResultFile()
    {
        ResultFilePath = null;
    }
}
