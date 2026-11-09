using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Application.Pagination;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AqtCoreFW.Forms.Infrastructure.Services;

/// <summary>
/// Json-mode IFormDataStorage implementation.
/// Submission data is already serialized into FormSubmission.SubmissionData by the command handler.
/// WriteAsync is a no-op here; ReadAsync deserializes SubmissionData from FormSubmissions table.
/// </summary>
public sealed class JsonFormDataStorage(FormsDbContext dbContext) : IFormDataStorage
{
    /// <summary>No-op — Json data is written directly in FormSubmission.SubmissionData.</summary>
    public Task WriteAsync(
        long templateId,
        Guid submissionId,
        Guid tenantId,
        int version,
        IDictionary<string, object?> values,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task<IDictionary<string, object?>?> ReadAsync(
        long templateId,
        Guid submissionId,
        CancellationToken cancellationToken = default)
    {
        // submissionId = PublicId (T2 external AK); FormTemplateId = long (T1 PK)
        var submission = await dbContext.FormSubmissions
            .FirstOrDefaultAsync(
                s => s.PublicId == submissionId && s.FormTemplateId == templateId && !s.IsDeleted,
                cancellationToken);

        if (submission is null || string.IsNullOrWhiteSpace(submission.SubmissionData))
            return null;

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(
                submission.SubmissionData,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return new Dictionary<string, object?>();
        }
    }

    /// <summary>
    /// No-op — Json-mode anonymization is handled by FormsPiiAnonymizer directly
    /// on FormSubmission.SubmissionData via EF Core.
    /// </summary>
    public Task AnonymizeAsync(
        long templateId,
        IReadOnlyList<Guid> submissionIds,
        IReadOnlyList<string> piiFieldKeys,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task<PagedResult<IDictionary<string, object?>>> QueryAsync(
        long templateId,
        Guid tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.FormSubmissions
            .Where(s => s.FormTemplateId == templateId
                        && s.TenantId == tenantId
                        && !s.IsDeleted);

        var total = await query.CountAsync(cancellationToken);

        var submissions = await query
            .OrderByDescending(s => s.SubmittedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = submissions
            .Select(s =>
            {
                IDictionary<string, object?> data;
                try
                {
                    data = string.IsNullOrWhiteSpace(s.SubmissionData)
                        ? new Dictionary<string, object?>()
                        : JsonSerializer.Deserialize<Dictionary<string, object?>>(s.SubmissionData)
                          ?? new Dictionary<string, object?>();
                }
                catch
                {
                    data = new Dictionary<string, object?>();
                }
                return data;
            })
            .ToList<IDictionary<string, object?>>();

        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        var meta = new PaginationMeta(page, pageSize, totalPages, null, null, page < totalPages);
        return new PagedResult<IDictionary<string, object?>>(items, total, meta);
    }
}
