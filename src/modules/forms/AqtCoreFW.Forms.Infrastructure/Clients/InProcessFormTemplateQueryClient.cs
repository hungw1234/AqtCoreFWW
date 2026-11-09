using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using Dapper;

namespace AqtCoreFW.Forms.Infrastructure.Clients;

/// <summary>
/// In-process implementation of <see cref="IFormTemplateQueryClient"/> (BP MVP P4 Slice 2B).
/// Dapper read on <c>forms.FormTemplateVersions JOIN forms.FormTemplates</c> (same-schema; no
/// arch-test violation). Defense-in-depth: TenantId filter on both sides of the JOIN (mirrors
/// <c>GetFormTemplateVersionQueryHandler</c> rationale — RLS may be bypassed by db_owner login per
/// [[phase6-rls-restore-shipped-260512]]).
///
/// Cross-tenant rows return null (no existence leak).
/// </summary>
public sealed class InProcessFormTemplateQueryClient(IReadDbConnection db)
    : IFormTemplateQueryClient
{
    public async Task<FormTemplateVersionSchemaDto?> GetVersionSchemaByInternalIdAsync(
        long formTemplateId,
        int versionNumber,
        Guid tenantId,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT TOP 1
                   t.PublicId AS FormTemplatePublicId,
                   v.VersionNumber,
                   v.SchemaJson
            FROM forms.FormTemplateVersions v
            INNER JOIN forms.FormTemplates t ON t.Id = v.FormTemplateId
            WHERE v.FormTemplateId = @FormTemplateId
              AND v.VersionNumber = @VersionNumber
              AND v.TenantId = @TenantId
              AND t.TenantId = @TenantId
              AND v.IsDeleted = 0
              AND t.IsDeleted = 0
            """;

        return await db.QueryFirstOrDefaultAsync<FormTemplateVersionSchemaDto>(
            sql,
            new
            {
                FormTemplateId = formTemplateId,
                VersionNumber = versionNumber,
                TenantId = tenantId
            },
            ct);
    }

    public async Task<FormTemplateVersionSchemaDto?> GetVersionSchemaByPublicIdAsync(
        Guid formTemplatePublicId,
        int versionNumber,
        Guid tenantId,
        CancellationToken ct = default)
    {
        // PublicId-keyed variant for BP MVP P6 prefill resolver. Same defense-in-depth tenant filter
        // on both JOIN sides (RLS bypass via db_owner login considered per [[phase6-rls-restore-shipped-260512]]).
        const string sql = """
            SELECT TOP 1
                   t.PublicId AS FormTemplatePublicId,
                   v.VersionNumber,
                   v.SchemaJson
            FROM forms.FormTemplateVersions v
            INNER JOIN forms.FormTemplates t ON t.Id = v.FormTemplateId
            WHERE t.PublicId = @FormTemplatePublicId
              AND v.VersionNumber = @VersionNumber
              AND v.TenantId = @TenantId
              AND t.TenantId = @TenantId
              AND v.IsDeleted = 0
              AND t.IsDeleted = 0
            """;

        return await db.QueryFirstOrDefaultAsync<FormTemplateVersionSchemaDto>(
            sql,
            new
            {
                FormTemplatePublicId = formTemplatePublicId,
                VersionNumber = versionNumber,
                TenantId = tenantId
            },
            ct);
    }

    /// <summary>
    /// BP MVP P7: resolve FormTemplate PublicId → internal bigint Id, scoped to tenant.
    /// Defense-in-depth TenantId filter (RLS bypass via db_owner login per phase6-rls-restore precedent).
    /// Returns null for not-found OR cross-tenant (no existence leak).
    /// </summary>
    public async Task<long?> TryResolveTemplateInternalIdAsync(
        Guid formTemplatePublicId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT TOP 1 t.Id
            FROM forms.FormTemplates t
            WHERE t.PublicId = @FormTemplatePublicId
              AND t.TenantId = @TenantId
              AND t.IsDeleted = 0
            """;

        var id = await db.QueryFirstOrDefaultAsync<long?>(
            sql,
            new { FormTemplatePublicId = formTemplatePublicId, TenantId = tenantId },
            ct);

        return id;
    }

    /// <summary>
    /// BUG2 fix 2026-05-18: batch internal-bigint-Id → Guid PublicId for the BP detail read path.
    /// Single Dapper IN-expansion (one round-trip, no N+1). Defense-in-depth TenantId filter
    /// (RLS bypass via db_owner login per phase6-rls-restore precedent). Cross-tenant / soft-deleted
    /// rows are excluded → omitted from the map (no existence leak).
    /// </summary>
    public async Task<IReadOnlyDictionary<long, Guid>> ResolveTemplatePublicIdsAsync(
        IReadOnlyCollection<long> formTemplateIds,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var map = new Dictionary<long, Guid>();
        if (formTemplateIds.Count == 0)
            return map;

        const string sql = """
            SELECT t.Id, t.PublicId
            FROM forms.FormTemplates t
            WHERE t.Id IN @FormTemplateIds
              AND t.TenantId = @TenantId
              AND t.IsDeleted = 0
            """;

        var rows = await db.QueryAsync<FormTemplateIdRow>(
            sql,
            new { FormTemplateIds = formTemplateIds, TenantId = tenantId },
            ct);

        foreach (var r in rows)
            map[r.Id] = r.PublicId;

        return map;
    }
}

/// <summary>
/// Dapper projection for the batch Id → PublicId resolve. Namespace-scoped (not nested) so
/// Dapper column-name mapping is unambiguous — mirrors the BusinessProcessRootRow precedent.
/// </summary>
internal sealed class FormTemplateIdRow
{
    public long Id { get; set; }
    public Guid PublicId { get; set; }
}
