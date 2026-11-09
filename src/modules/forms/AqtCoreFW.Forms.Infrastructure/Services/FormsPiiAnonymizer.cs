using System.Text.Json;
using System.Text.Json.Nodes;
using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Contracts.Rtbf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Forms.Infrastructure.Services;

/// <summary>
/// RTBF anonymizer for Forms module.
/// Json mode: loads FormSubmission entities, replaces IsPii field values in SubmissionData JSON.
/// Table mode: calls TableFormDataStorage.AnonymizeAsync() via Dapper bulk UPDATE on dynamic table.
/// Guest support: when citizenNationalId provided, scans JSON submissions for matching value
///               in IsPii fields (for SubmittedBy = Guid.Empty guest submissions).
/// Idempotent: skips submissions with IsAnonymized = true.
/// </summary>
public sealed class FormsPiiAnonymizer(
    FormsDbContext db,
    IFormDataStorageFactory storageFactory,
    ILogger<FormsPiiAnonymizer> logger) : IModulePiiAnonymizer
{
    internal const string Sentinel = "[DA AN DANH]";

    public string ModuleName => "Forms";

    public async Task<RtbfAnonymizationResult> AnonymizeAsync(
        Guid dataSubjectId,
        Guid tenantId,
        string? citizenNationalId,
        CancellationToken cancellationToken = default)
    {
        // Load all active form templates with their IsPii fields in this tenant
        var templates = await db.Set<AqtCoreFW.Forms.Domain.Entities.FormTemplate>()
            .Where(t => t.TenantId == tenantId)
            .Include(t => t.Fields.Where(f => f.IsPii && f.IsActive))
            .ToListAsync(cancellationToken);

        int total = 0;

        foreach (var template in templates)
        {
            var piiFields = template.Fields.Where(f => f.IsPii && f.IsActive).ToList();
            if (piiFields.Count == 0) continue;

            var piiKeys = piiFields.Select(f => f.FieldKey).ToList();

            // Load non-anonymized submissions by this subject
            var submissions = await db.Set<AqtCoreFW.Forms.Domain.Entities.FormSubmission>()
                .Where(s => s.FormTemplateId == template.Id
                            && s.TenantId == tenantId
                            && !s.IsAnonymized
                            && s.SubmittedBy == dataSubjectId)
                .ToListAsync(cancellationToken);

            // Guest submissions: scan JSON content for citizenNationalId match
            if (!string.IsNullOrEmpty(citizenNationalId))
            {
                var guestSubmissions = await db.Set<AqtCoreFW.Forms.Domain.Entities.FormSubmission>()
                    .Where(s => s.FormTemplateId == template.Id
                                && s.TenantId == tenantId
                                && !s.IsAnonymized
                                && s.SubmittedBy == Guid.Empty
                                && s.SubmissionData != null
                                && s.SubmissionData.Contains(citizenNationalId))
                    .ToListAsync(cancellationToken);

                submissions.AddRange(guestSubmissions);
            }

            if (submissions.Count == 0) continue;

            var storage = storageFactory.Create(template.StorageMode);

            if (template.StorageMode == StorageMode.Json)
            {
                foreach (var sub in submissions)
                {
                    if (sub.SubmissionData is null) continue;
                    var anonymized = AnonymizeJson(sub.SubmissionData, piiKeys);
                    sub.AnonymizeSubmissionData(anonymized);
                }
                await db.SaveChangesAsync(cancellationToken);
                total += submissions.Count;
            }
            else // Table mode
            {
                // Use PublicId (Guid, T2 external AK) — AnonymizeAsync operates on PublicId column
                var submissionIds = submissions.Select(s => s.PublicId).ToList();
                await storage.AnonymizeAsync(template.Id, submissionIds, piiKeys, cancellationToken);

                // Mark EF records as anonymized
                foreach (var sub in submissions) sub.AnonymizeSubmissionData(string.Empty);
                await db.SaveChangesAsync(cancellationToken);
                total += submissions.Count;
            }

            logger.LogInformation(
                "RTBF Forms: anonymized {Count} submissions for template {TemplateId}",
                submissions.Count, template.Id);
        }

        return RtbfAnonymizationResult.Ok(ModuleName, total);
    }

    /// <summary>
    /// Replace leaf values of IsPii keys with Sentinel in a JSON object.
    /// Non-PII keys are preserved as-is.
    /// </summary>
    private static string AnonymizeJson(string json, IReadOnlyList<string> piiKeys)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node is not JsonObject obj) return json;

            foreach (var key in piiKeys)
            {
                if (obj.ContainsKey(key))
                    obj[key] = JsonValue.Create(Sentinel);
            }

            return obj.ToJsonString();
        }
        catch (JsonException)
        {
            // Malformed JSON — return sentinel blob rather than leaving PII intact
            return $"{{\"_anonymized\":\"{Sentinel}\"}}";
        }
    }
}
