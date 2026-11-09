using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Forms.Application.Queries.GetFormTemplateVersions;

/// <summary>
/// List all snapshots of a FormTemplate, newest first.
/// TemplatePublicId = FormTemplate.PublicId (T2 external AK).
/// </summary>
public sealed record ListFormTemplateVersionsQuery(
    Guid TemplatePublicId,
    Guid TenantId) : IQuery<IReadOnlyList<FormTemplateVersionSummaryDto>>;

/// <summary>Get a single snapshot by version number (full SchemaJson).</summary>
public sealed record GetFormTemplateVersionQuery(
    Guid TemplatePublicId,
    int VersionNumber,
    Guid TenantId) : IQuery<FormTemplateVersionDto>;
