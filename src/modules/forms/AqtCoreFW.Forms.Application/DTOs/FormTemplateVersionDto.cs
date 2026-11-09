namespace AqtCoreFW.Forms.Application.DTOs;

/// <summary>List-row projection — omits SchemaJson to keep payload small.</summary>
public sealed record FormTemplateVersionSummaryDto(
    Guid TemplatePublicId,
    int VersionNumber,
    DateTimeOffset CreatedAt,
    Guid CreatedBy);

/// <summary>Single-snapshot read — includes full SchemaJson string (caller parses).</summary>
public sealed record FormTemplateVersionDto(
    Guid TemplatePublicId,
    int VersionNumber,
    string SchemaJson,
    DateTimeOffset CreatedAt,
    Guid CreatedBy);
