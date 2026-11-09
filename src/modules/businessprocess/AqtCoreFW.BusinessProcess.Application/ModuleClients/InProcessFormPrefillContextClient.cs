using System.Diagnostics;
using System.Text.Json;
using AqtCoreFW.BusinessProcess.Application.Telemetry;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.BusinessProcess.Application.ModuleClients;

/// <summary>
/// BP MVP P6: cross-module prefill resolver. Walks each target FormField's <c>PrefillSource</c> envelope
/// captured in the published <c>FormTemplateVersion</c> snapshot, looks up the source field value from a
/// prior submission in the same BP-Run (latest per source FormTemplate via <c>GetLatestForRunAsync</c>),
/// and returns a <c>{fieldKey -&gt; value}</c> dict.
///
/// <list type="bullet">
///   <item><description>Tenant guard via <see cref="IWorkflowModuleClient.GetTenantForInstanceAsync"/> —
///         null/mismatch -&gt; returns null so the controller maps to 404 (D8).</description></item>
///   <item><description>Per-field unresolvable values are LogWarning + omitted (D4 partial-fail tolerance).
///         Cases: missing source submission, missing source field, malformed PrefillSource JSON.</description></item>
///   <item><description>Storage backend (Json vs dynamic Table) is hidden behind
///         <see cref="IFormSubmissionRunClient.GetFullSubmissionAsync"/>. P6-FU2 (2026-05-16):
///         GetFullSubmissionAsync normalizes JsonElement → native CLR at the boundary, so this
///         resolver sees uniform types across both backends (supersedes D9 from P6 plan).</description></item>
///   <item><description>R3 N+1 mitigation: dedup unique source submission ids before fetching, then cache
///         per submission for the call duration.</description></item>
/// </list>
/// </summary>
public sealed class InProcessFormPrefillContextClient(
    IWorkflowModuleClient workflowClient,
    IFormTemplateQueryClient formTemplateClient,
    IFormSubmissionRunClient submissionRunClient,
    ILogger<InProcessFormPrefillContextClient> logger)
    : IFormPrefillContextClient
{
    public async Task<IReadOnlyDictionary<string, object?>?> GetPrefillContextAsync(
        Guid workflowInstanceId,
        Guid targetFormTemplatePublicId,
        int targetFormTemplateVersion,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        string outcome = "ok";
        try
        {
            // (1) Root tenant guard: null when instance does not exist OR cross-tenant. Caller maps to 404.
            var instanceTenant = await workflowClient.GetTenantForInstanceAsync(workflowInstanceId, ct);
            if (instanceTenant is null || instanceTenant.Value != tenantId)
            {
                outcome = "not_found";
                logger.LogInformation(
                    "Prefill context not found: workflowInstanceId={WorkflowInstanceId}, callerTenant={CallerTenant}, instanceTenant={InstanceTenant}",
                    workflowInstanceId, tenantId, instanceTenant);
                return null;
            }

            // (2) Target snapshot — only the published schema may carry prefill metadata. Missing snapshot
            // = treat as "no prefill resolvable" (200 + empty dict). Distinct from instance-not-found.
            var targetSnapshot = await formTemplateClient.GetVersionSchemaByPublicIdAsync(
                targetFormTemplatePublicId, targetFormTemplateVersion, tenantId, ct);

            if (targetSnapshot is null)
            {
                logger.LogWarning(
                    "Prefill: target snapshot missing — formId={FormId} v{Version} tenant={Tenant} (returning empty dict).",
                    targetFormTemplatePublicId, targetFormTemplateVersion, tenantId);
                return new Dictionary<string, object?>();
            }

            var prefillTargets = ParsePrefillTargets(targetSnapshot.SchemaJson, targetFormTemplatePublicId);
            if (prefillTargets.Count == 0)
                return new Dictionary<string, object?>();

            // (3) Latest submission per source FormTemplate inside the run. SQL-side ROW_NUMBER picks the
            // most recent — caller does O(1) lookup by FormTemplatePublicId.
            var latest = await submissionRunClient.GetLatestForRunAsync(workflowInstanceId, tenantId, ct);
            var byTemplateId = latest.ToDictionary(s => s.FormTemplatePublicId, s => s);

            // (4) Dedup source submission ids before per-submission fetch (R3 mitigation).
            var neededSubmissions = prefillTargets
                .Select(t => byTemplateId.TryGetValue(t.Source.SourceFormTemplatePublicId, out var sub) ? (Guid?)sub.Id : null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct()
                .ToList();

            // (5) One fetch per unique source submission. GetFullSubmissionAsync hides Json/Table mode (D3).
            var dataBySubmission = new Dictionary<Guid, IReadOnlyDictionary<string, object?>>();
            foreach (var subId in neededSubmissions)
            {
                var data = await submissionRunClient.GetFullSubmissionAsync(subId, tenantId, ct);
                if (data is not null)
                    dataBySubmission[subId] = data;
            }

            // (6) Resolve each target field from cached source dicts.
            var result = new Dictionary<string, object?>();
            foreach (var target in prefillTargets)
            {
                if (!byTemplateId.TryGetValue(target.Source.SourceFormTemplatePublicId, out var sourceSummary))
                {
                    logger.LogWarning(
                        "Prefill: source FormTemplate {SourceFormId} not found in run {RunId} for target field {Key} — omitted.",
                        target.Source.SourceFormTemplatePublicId, workflowInstanceId, target.TargetFieldKey);
                    continue;
                }

                if (!dataBySubmission.TryGetValue(sourceSummary.Id, out var sourceData))
                {
                    logger.LogWarning(
                        "Prefill: failed to load source submission {SubmissionId} for target field {Key} — omitted.",
                        sourceSummary.Id, target.TargetFieldKey);
                    continue;
                }

                if (!sourceData.TryGetValue(target.Source.SourceFieldKey, out var value))
                {
                    logger.LogWarning(
                        "Prefill: source field key {SourceKey} absent in submission {SubmissionId} for target field {TargetKey} — omitted.",
                        target.Source.SourceFieldKey, sourceSummary.Id, target.TargetFieldKey);
                    continue;
                }

                result[target.TargetFieldKey] = value;
            }

            return result;
        }
        finally
        {
            stopwatch.Stop();
            BusinessProcessMetrics.PrefillDurationMs.Record(
                stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("tenant", tenantId),
                new KeyValuePair<string, object?>("result", outcome));
        }
    }

    /// <summary>
    /// Extract every active field with a parseable PrefillSource envelope. Self-target (source form ==
    /// target form) is skipped per spec (1-hop only, R2 circular guard). Malformed envelopes log + skip
    /// without poisoning the dict.
    /// </summary>
    private List<PrefillTarget> ParsePrefillTargets(string schemaJson, Guid targetFormTemplatePublicId)
    {
        var targets = new List<PrefillTarget>();
        SchemaSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<SchemaSnapshot>(
                schemaJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            logger.LogError(ex,
                "Prefill: target schema JSON is malformed for form {FormId} — returning no targets.",
                targetFormTemplatePublicId);
            return targets;
        }

        if (snapshot?.Fields is null) return targets;

        foreach (var field in snapshot.Fields)
        {
            if (string.IsNullOrWhiteSpace(field.PrefillSource)) continue;

            var meta = PrefillSourceMeta.TryParse(field.PrefillSource);
            if (meta is null)
            {
                logger.LogWarning(
                    "GOV_BP_P6_001: invalid PrefillSource JSON on field {Key} (form {FormId}).",
                    field.Key, targetFormTemplatePublicId);
                continue;
            }

            // 1-hop only: self-targeting fields silently skipped (caller would loop on the same form).
            if (meta.SourceFormTemplatePublicId == targetFormTemplatePublicId)
            {
                logger.LogWarning(
                    "Prefill: self-targeting source on field {Key} (form {FormId}) — omitted (1-hop only).",
                    field.Key, targetFormTemplatePublicId);
                continue;
            }

            targets.Add(new PrefillTarget(field.Key, meta));
        }

        return targets;
    }

    private sealed record PrefillTarget(string TargetFieldKey, PrefillSourceMeta Source);

    // Snapshot envelope shape — must align with FormTemplateVersionFactory.FieldPayload key names.
    // Only the keys we need are read; unknown keys are ignored (forward-compatible).
    private sealed record SchemaSnapshot(List<SnapshotField>? Fields);
    private sealed record SnapshotField(string Key, string? PrefillSource);
}
