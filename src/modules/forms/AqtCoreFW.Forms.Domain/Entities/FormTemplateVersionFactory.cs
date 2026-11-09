using System.Text.Json;

namespace AqtCoreFW.Forms.Domain.Entities;

/// <summary>
/// Builds FormTemplateVersion snapshots from a FormTemplate aggregate at Publish time.
/// SchemaJson captures all active fields with validation/visibility/PII metadata.
/// Versioned by the outer "schemaVersion" key for forward-compatible reads.
/// </summary>
public static class FormTemplateVersionFactory
{
    /// <summary>
    /// Snapshot schema envelope version. Bump when adding/removing top-level keys so
    /// downstream readers can branch on shape changes (BP renderer, prefill resolver).
    /// </summary>
    public const int SchemaEnvelopeVersion = 1;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    /// <summary>
    /// Build an immutable snapshot from a tracked FormTemplate. Caller is responsible for
    /// persisting the returned entity in the same UnitOfWork as the Publish status flip.
    /// </summary>
    public static FormTemplateVersion Snapshot(FormTemplate template, Guid createdBy)
    {
        ArgumentNullException.ThrowIfNull(template);

        var payload = new SchemaPayload(
            SchemaEnvelopeVersion: SchemaEnvelopeVersion,
            Version: template.Version,
            StorageMode: template.StorageMode.ToString(),
            RequiresConsent: template.RequiresConsent,
            ApprovalWorkflowDefinitionId: template.ApprovalWorkflowDefinitionId,
            Fields: template.Fields
                .Where(f => f.IsActive)
                .OrderBy(f => f.DisplayOrder)
                .Select(f => new FieldPayload(
                    Id: f.Id,
                    Key: f.FieldKey,
                    Type: f.Type.ToString(),
                    LabelVi: f.LabelVi,
                    LabelEn: f.LabelEn,
                    DisplayOrder: f.DisplayOrder,
                    Required: f.Required,
                    IsPii: f.IsPii,
                    Validation: f.ValidationRulesJson,
                    Visibility: f.VisibilityRulesJson,
                    RequiredIf: f.RequiredIfJson,
                    DataSource: f.DataSourceJson,
                    Formula: f.FormulaJson,
                    ParentFieldId: f.ParentFieldId,
                    PrefillSource: f.PrefillSourceJson,
                    // Static EnumRef/Radio choices. Eager-loaded by the publish repository
                    // (.ThenInclude(f => f.Options)); without them an EnumRef Select renders empty.
                    Options: f.Options.Count > 0
                        ? f.Options
                            .OrderBy(o => o.DisplayOrder)
                            .Select(o => new OptionPayload(o.Id, o.Value, o.LabelVi, o.LabelEn, o.DisplayOrder))
                            .ToList()
                        : null))
                .ToList());

        var schemaJson = JsonSerializer.Serialize(payload, JsonOpts);

        return FormTemplateVersion.CreateSnapshot(
            template.TenantId,
            template.Id,
            template.Version,
            schemaJson,
            createdBy);
    }

    /// <summary>
    /// Publish the template AND build its immutable snapshot as ONE call. The two MUST happen
    /// together — a status flip to Active without a FormTemplateVersions row is a data-integrity
    /// bug (BP intake/state-form resolution then 404s at end-user runtime). Root-caused 2026-05-18:
    /// E2ETestDataSeeder called <see cref="FormTemplate.Publish"/> directly and skipped the snapshot
    /// (the snapshot logic lived only in the command handler). Both the handler and the seeder now
    /// call this so the pairing can't drift again. Caller still persists both in one UnitOfWork.
    /// </summary>
    public static FormTemplateVersion PublishAndSnapshot(FormTemplate template, Guid actorId)
    {
        ArgumentNullException.ThrowIfNull(template);
        template.Publish(actorId);
        return Snapshot(template, actorId);
    }

    // Internal payload records — kept private to fix JSON shape contract.
    private sealed record SchemaPayload(
        int SchemaEnvelopeVersion,
        int Version,
        string StorageMode,
        bool RequiresConsent,
        Guid? ApprovalWorkflowDefinitionId,
        IReadOnlyList<FieldPayload> Fields);

    // BP MVP P6: PrefillSource added as additive nullable field. SchemaEnvelopeVersion stays 1 —
    // older readers ignore unknown keys; resolver reads from the published snapshot, not the live entity.
    // Options follows the same additive-nullable convention (null when the field carries no choices).
    private sealed record FieldPayload(
        long Id,
        string Key,
        string Type,
        string LabelVi,
        string LabelEn,
        int DisplayOrder,
        bool Required,
        bool IsPii,
        string? Validation,
        string? Visibility,
        string? RequiredIf,
        string? DataSource,
        string? Formula,
        long? ParentFieldId,
        string? PrefillSource,
        IReadOnlyList<OptionPayload>? Options);

    // Static dropdown/radio choices snapshotted inline so the renderer needs no live entity read.
    private sealed record OptionPayload(
        long Id,
        string Value,
        string LabelVi,
        string LabelEn,
        int DisplayOrder);
}
