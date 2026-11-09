using AqtCoreFW.Audit.Domain.Repositories;
using AqtCoreFW.SharedKernel.Contracts.Rtbf;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Audit.Application.Commands.ProcessRtbfRequest;

/// <summary>
/// Orchestrates RTBF PII anonymization across all modules (Law 91/2025 Art.9).
/// Resolves IEnumerable&lt;IModulePiiAnonymizer&gt; and calls each sequentially.
/// Atomicity: best-effort — partial failures are recorded in RtbfRequest.FailureLog.
/// Job is idempotent; operators can safely re-run to complete remaining modules.
/// </summary>
public sealed class ProcessRtbfRequestCommandHandler(
    IRtbfRequestRepository repository,
    IEnumerable<IModulePiiAnonymizer> anonymizers,
    ILogger<ProcessRtbfRequestCommandHandler> logger)
    : IRequestHandler<ProcessRtbfRequestCommand, Result>
{
    private const string Sentinel = "[DA AN DANH]";

    public async Task<Result> Handle(ProcessRtbfRequestCommand request, CancellationToken cancellationToken)
    {
        var rtbf = await repository.GetByIdAsync(request.RequestId, cancellationToken);
        if (rtbf is null)
            return Result.Fail(new NotFoundError($"RtbfRequest {request.RequestId} not found"));

        rtbf.StartProcessing();
        await repository.SaveChangesAsync(cancellationToken);

        var failures = new List<string>();
        int totalRecords = 0;

        foreach (var anonymizer in anonymizers)
        {
            try
            {
                var result = await anonymizer.AnonymizeAsync(
                    rtbf.DataSubjectId,
                    rtbf.TenantId,
                    rtbf.CitizenNationalId,
                    cancellationToken);

                if (result.Succeeded)
                {
                    totalRecords += result.RecordsAnonymized;
                    logger.LogInformation(
                        "RTBF {RequestId}: {Module} anonymized {Count} records",
                        request.RequestId, anonymizer.ModuleName, result.RecordsAnonymized);
                }
                else
                {
                    failures.Add($"{anonymizer.ModuleName}: {result.ErrorMessage}");
                    logger.LogWarning(
                        "RTBF {RequestId}: {Module} failed — {Error}",
                        request.RequestId, anonymizer.ModuleName, result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{anonymizer.ModuleName}: {ex.Message}");
                logger.LogError(ex,
                    "RTBF {RequestId}: {Module} threw unexpected exception",
                    request.RequestId, anonymizer.ModuleName);
            }
        }

        if (failures.Count == 0)
            rtbf.Complete(request.ProcessedBy);
        else
            rtbf.PartiallyComplete(request.ProcessedBy, string.Join(" | ", failures));

        await repository.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "RTBF {RequestId} finished: status={Status}, records={Total}, failures={FailCount}",
            request.RequestId, rtbf.Status, totalRecords, failures.Count);

        return Result.Ok();
    }
}
