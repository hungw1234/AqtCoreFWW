using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Forms.Application.Queries.GetFormAnalytics;

/// <summary>
/// Returns aggregate stats for a form template (last 30 days submissions by date).
/// TemplatePublicId = FormTemplate.PublicId (T2 Guid AK — external addressing).
/// </summary>
public sealed record GetFormAnalyticsQuery(Guid TemplatePublicId, Guid TenantId)
    : IQuery<FormAnalyticsDto>;

public sealed record FormAnalyticsDto(
    int TotalSubmissions,
    int PendingCount,
    int ApprovedCount,
    int RejectedCount,
    double AvgFieldsFilled,
    IReadOnlyList<SubmissionsByDateDto> SubmissionsByDate);

public sealed record SubmissionsByDateDto(string Date, int Count);
