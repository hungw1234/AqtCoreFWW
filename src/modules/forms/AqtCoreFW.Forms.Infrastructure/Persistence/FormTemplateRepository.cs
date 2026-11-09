using System.Data;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Forms.Infrastructure.Persistence;

public sealed class FormTemplateRepository(
    FormsDbContext dbContext,
    ICrossModuleUnitOfWork? uow = null)
    : GenericRepository<FormTemplate, long>(dbContext, uow), IFormTemplateRepository
{
    private FormsDbContext Db => (FormsDbContext)Context;

    public override async Task<Result<FormTemplate>> GetByIdAsync(
        long id, CancellationToken cancellationToken = default)
    {
        await EnsureEnlistedAsync(cancellationToken);
        var entity = await Db.FormTemplates
            .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, cancellationToken);

        return entity is null
            ? Result.Fail(new NotFoundError($"FormTemplate {id} not found."))
            : Result.Ok(entity);
    }

    public async Task<FormTemplate?> GetByPublicIdAsync(
        Guid publicId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        await EnsureEnlistedAsync(cancellationToken);
        return await Db.FormTemplates
            .FirstOrDefaultAsync(
                t => t.PublicId == publicId && t.TenantId == tenantId && !t.IsDeleted,
                cancellationToken);
    }

    public async Task<FormTemplate?> GetByPublicIdWithFieldsAsync(
        Guid publicId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        await EnsureEnlistedAsync(cancellationToken);
        return await Db.FormTemplates
            .Include(t => t.Fields)
                .ThenInclude(f => f.Options)
            .Include(t => t.SignerRoles)
            .FirstOrDefaultAsync(
                t => t.PublicId == publicId && t.TenantId == tenantId && !t.IsDeleted,
                cancellationToken);
    }

    public async Task<FormTemplate?> GetActiveByIdAsync(
        long id, Guid tenantId, CancellationToken cancellationToken = default)
    {
        await EnsureEnlistedAsync(cancellationToken);
        return await Db.FormTemplates
            .Include(t => t.Fields)
                .ThenInclude(f => f.Options)
            .Include(t => t.SignerRoles)
            .FirstOrDefaultAsync(
                t => t.Id == id && t.TenantId == tenantId
                     && t.Status == FormTemplateStatus.Active && !t.IsDeleted,
                cancellationToken);
    }

    public async Task<FormTemplate?> GetWithFieldsAsync(
        long id, Guid tenantId, CancellationToken cancellationToken = default)
    {
        await EnsureEnlistedAsync(cancellationToken);
        return await Db.FormTemplates
            .Include(t => t.Fields)
                .ThenInclude(f => f.Options)
            .Include(t => t.SignerRoles)
            .FirstOrDefaultAsync(
                t => t.Id == id && t.TenantId == tenantId && !t.IsDeleted,
                cancellationToken);
    }

    public async Task<FormTemplate?> GetPublicWithFieldsAsync(
        long id, Guid tenantId, CancellationToken cancellationToken = default)
    {
        await EnsureEnlistedAsync(cancellationToken);

        var conn = (SqlConnection)Db.Database.GetDbConnection();
        // When we own the open (anonymous public path — no active UoW), the interceptor left
        // SESSION_CONTEXT unset (no ambient tenant), so the FormTemplates RLS FILTER would return
        // 0 rows under a non-db_owner login. Pin the server-resolved tenant on THIS connection for
        // the read; the pool reset (sp_reset_connection) clears it on close. When the connection is
        // already open (enlisted on an active UoW), BeginAsync already pinned the tenant — do NOT
        // re-pin or close a connection we do not own.
        var openedHere = conn.State != ConnectionState.Open;
        if (openedHere)
        {
            await Db.Database.OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "EXEC sp_set_session_context N'TenantId', @tenantId";
            cmd.Parameters.Add(new SqlParameter("@tenantId", tenantId.ToString()));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            return await Db.FormTemplates
                .Include(t => t.Fields)
                    .ThenInclude(f => f.Options)
                .Include(t => t.SignerRoles)
                .FirstOrDefaultAsync(
                    t => t.Id == id && t.TenantId == tenantId && !t.IsDeleted,
                    cancellationToken);
        }
        finally
        {
            if (openedHere) await Db.Database.CloseConnectionAsync();
        }
    }

    public async Task<FormTemplate?> GetByPublicIdWithSignerRolesAsync(
        Guid publicId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        await EnsureEnlistedAsync(cancellationToken);
        return await Db.FormTemplates
            .Include(t => t.SignerRoles)
            .FirstOrDefaultAsync(
                t => t.PublicId == publicId && t.TenantId == tenantId && !t.IsDeleted,
                cancellationToken);
    }

    public async Task<FormTemplate?> GetByCodeAsync(
        string code, Guid tenantId, CancellationToken cancellationToken = default)
    {
        await EnsureEnlistedAsync(cancellationToken);
        return await Db.FormTemplates
            .FirstOrDefaultAsync(
                t => t.Code == code && t.TenantId == tenantId && !t.IsDeleted,
                cancellationToken);
    }

    public async Task<bool> HasSubmissionsAsync(
        long templateId, CancellationToken cancellationToken = default)
    {
        await EnsureEnlistedAsync(cancellationToken);
        return await Db.FormSubmissions
            .AnyAsync(s => s.FormTemplateId == templateId && !s.IsDeleted, cancellationToken);
    }

    public async Task<IReadOnlyList<FormTemplate>> GetMaterializingAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureEnlistedAsync(cancellationToken);
        return await Db.FormTemplates
            .Include(t => t.Fields)
                .ThenInclude(f => f.Options)
            .Where(t => t.Status == FormTemplateStatus.Materializing && !t.IsDeleted)
            .ToListAsync(cancellationToken);
    }

    public async Task AddVersionAsync(
        FormTemplateVersion version, CancellationToken cancellationToken = default)
    {
        await EnsureEnlistedAsync(cancellationToken);
        await Db.FormTemplateVersions.AddAsync(version, cancellationToken);
    }
}
