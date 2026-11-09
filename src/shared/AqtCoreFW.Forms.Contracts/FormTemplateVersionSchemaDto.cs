namespace AqtCoreFW.SharedKernel.Contracts.Clients;

/// <summary>
/// Cross-module transport DTO for a pinned FormTemplate snapshot.
/// FormTemplatePublicId is the T2 Guid AK (stable across versions); VersionNumber pins the snapshot
/// row in <c>forms.FormTemplateVersions</c>. SchemaJson is the raw JSON string — caller parses.
/// Audit fields omitted by design (contract surface, not Forms-internal projection).
/// </summary>
public sealed record FormTemplateVersionSchemaDto(
    Guid FormTemplatePublicId,
    int VersionNumber,
    string SchemaJson);
