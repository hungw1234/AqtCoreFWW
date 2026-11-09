using AqtCoreFW.Reporting.Application.DTOs;

namespace AqtCoreFW.Reporting.Application.Abstractions;

/// <summary>
/// Generates a GOV compliance evidence PDF (QuestPDF).
/// Implementation lives in Infrastructure — decouples Application from QuestPDF.
/// </summary>
public interface ICompliancePdfGenerator
{
    Task<byte[]> GenerateAsync(ComplianceEvidenceDto data, CancellationToken ct = default);
}
