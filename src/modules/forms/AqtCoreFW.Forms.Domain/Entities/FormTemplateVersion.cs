using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Forms.Domain.Entities;

/// <summary>
/// Immutable snapshot of a FormTemplate schema captured at Publish time.
/// BusinessProcess and other consumers reference (FormTemplateId, VersionNumber) so their
/// configuration is stable even if the underlying template is later edited or re-published.
/// No update methods — append-only by design.
/// </summary>
public sealed class FormTemplateVersion : AuditableEntity, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public long FormTemplateId { get; private set; }
    public int VersionNumber { get; private set; }
    public string SchemaJson { get; private set; } = string.Empty;

    private FormTemplateVersion() { } // EF Core

    /// <summary>
    /// Domain factory — internal to the assembly so only FormTemplateVersionFactory can build snapshots.
    /// </summary>
    internal static FormTemplateVersion CreateSnapshot(
        Guid tenantId,
        long formTemplateId,
        int versionNumber,
        string schemaJson,
        Guid createdBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaJson);
        if (versionNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(versionNumber), "VersionNumber must be >= 1.");

        var v = new FormTemplateVersion
        {
            TenantId = tenantId,
            FormTemplateId = formTemplateId,
            VersionNumber = versionNumber,
            SchemaJson = schemaJson
        };
        v.SetAuditCreate(createdBy);
        return v;
    }
}
