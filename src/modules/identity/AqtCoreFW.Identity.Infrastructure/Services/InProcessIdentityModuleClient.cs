using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using Dapper;

namespace AqtCoreFW.Identity.Infrastructure.Services;

/// <summary>
/// Monolith in-process adapter for IIdentityModuleClient.
/// User lookups use Dapper for performance; permission-code resolution delegates to
/// IEffectivePermissionService (Redis-cached).
/// Swap to GrpcIdentityModuleClient when module is extracted (Phase 2+).
/// </summary>
public sealed class InProcessIdentityModuleClient(
    IReadDbConnection db,
    IEffectivePermissionService permissionService,
    EmailHashService emailHashService,
    IEncryptedFieldDecryptor decryptor) : IIdentityModuleClient
{
    private const int ChunkSize = 500;

    /// <summary>
    /// [RT-05/VAL-01] Find user by email, optionally scoped to tenant.
    /// Returns UserInfoDto with full user info (not just UserId/TenantId).
    ///
    /// QĐ742 §4.4 Wave-3: Email column is AES-GCM encrypted — plaintext WHERE comparisons
    /// no longer work. Lookup is now by NormalizedEmailHash (HMAC-SHA-256 with per-tenant pepper).
    /// Null tenantId → Guid.Empty sentinel pepper (cross-tenant / AuthServer login path).
    /// </summary>
    public async Task<UserInfoDto?> FindByEmailAsync(string email, Guid? tenantId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        var normalizedEmail = email.ToUpperInvariant().Trim();
        var emailHash = emailHashService.ComputeHash(normalizedEmail, tenantId);

        var sql = """
            SELECT TOP 1 Id AS UserId, FullName, TenantId, PrimaryOrgUnitId
            FROM [identity].AspNetUsers
            WHERE NormalizedEmailHash = @EmailHash
            """;

        var parameters = new DynamicParameters();
        parameters.Add("EmailHash", emailHash);

        if (tenantId.HasValue)
        {
            sql += " AND TenantId = @TenantId";
            parameters.Add("TenantId", tenantId.Value);
        }

        return await db.QueryFirstOrDefaultAsync<UserInfoDto>(sql, parameters, ct);
    }

    /// <summary>
    /// Batch lookup user info by IDs with chunking (SQL Server IN clause limit ~2100 params).
    /// [RT-02] Optional tenant filter. [RT-09] Hard cap 5000 IDs.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, UserInfoDto>> GetUserInfoByIdsAsync(
        IEnumerable<Guid> userIds, Guid? tenantId = null, CancellationToken ct = default)
    {
        var idList = userIds?.Distinct().ToList();
        if (idList is null || idList.Count == 0)
            return new Dictionary<Guid, UserInfoDto>();

        if (idList.Count > IIdentityModuleClient.MaxBatchSize)
            throw new ArgumentException(
                $"Batch size {idList.Count} exceeds maximum {IIdentityModuleClient.MaxBatchSize}.",
                nameof(userIds));

        var sql = """
            SELECT Id AS UserId, Email, FullName, TenantId, PrimaryOrgUnitId
            FROM [identity].AspNetUsers
            WHERE Id IN @Ids
            """;

        // Fallback: accept global (NULL-tenant) users as well as tenant-specific matches.
        // Seed/system users (e.g. dev viewer) carry TenantId=NULL; strict filter would hide
        // their display name from every enrichment caller. Not a leak — caller already holds
        // the UserId and the preceding join scoped by tenant.
        if (tenantId.HasValue)
            sql += " AND (TenantId IS NULL OR TenantId = @TenantId)";

        var result = new Dictionary<Guid, UserInfoDto>(idList.Count);

        foreach (var chunk in idList.Chunk(ChunkSize))
        {
            var parameters = new DynamicParameters();
            parameters.Add("Ids", chunk);
            if (tenantId.HasValue)
                parameters.Add("TenantId", tenantId.Value);

            // UserInfoDto is a class (not record) — safe for Dapper materialization [RT-12]
            var rows = await db.QueryAsync<UserInfoDto>(sql, parameters, ct);
            foreach (var row in rows)
            {
                // Email is AES-GCM encrypted at rest; the Dapper SELECT bypasses the EF converter, so
                // decrypt before any enrichment caller surfaces it as a display fallback. Email is
                // init-only — rebuild rather than mutate to keep the contract DTO immutable.
                result[row.UserId] = new UserInfoDto
                {
                    UserId = row.UserId,
                    Email = decryptor.Decrypt(row.Email),
                    FullName = row.FullName,
                    TenantId = row.TenantId,
                    PrimaryOrgUnitId = row.PrimaryOrgUnitId,
                };
            }
        }

        return result;
    }

    /// <summary>
    /// Row shape for the phone-number projection. Private because two columns do not warrant a
    /// contract DTO, and the decrypted value is rebuilt into the returned dictionary anyway.
    /// </summary>
    private sealed class PhoneRow
    {
        public Guid UserId { get; init; }
        public string? PhoneNumber { get; init; }
    }

    /// <summary>
    /// Batch lookup of decrypted phone numbers. Mirrors <see cref="GetUserInfoByIdsAsync"/>'s
    /// batching, chunking and tenant filter; see the contract for why the number is not carried
    /// on UserInfoDto.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, string?>> GetUserPhoneNumbersByIdsAsync(
        IEnumerable<Guid> userIds, Guid tenantId, CancellationToken ct = default)
    {
        var idList = userIds?.Distinct().ToList();
        if (idList is null || idList.Count == 0)
            return new Dictionary<Guid, string?>();

        if (idList.Count > IIdentityModuleClient.MaxBatchSize)
            throw new ArgumentException(
                $"Batch size {idList.Count} exceeds maximum {IIdentityModuleClient.MaxBatchSize}.",
                nameof(userIds));

        // Always tenant-scoped, and deliberately WITHOUT the "TenantId IS NULL OR" allowance the
        // sibling lookups grant seed/system users: that allowance exists so every tenant can render a
        // system user's display name, which is not a reason to hand out their phone number.
        const string sql = """
            SELECT Id AS UserId, PhoneNumber
            FROM [identity].AspNetUsers
            WHERE Id IN @Ids AND TenantId = @TenantId
            """;

        var result = new Dictionary<Guid, string?>(idList.Count);

        foreach (var chunk in idList.Chunk(ChunkSize))
        {
            var parameters = new DynamicParameters();
            parameters.Add("Ids", chunk);
            parameters.Add("TenantId", tenantId);

            var rows = await db.QueryAsync<PhoneRow>(sql, parameters, ct);
            foreach (var row in rows)
            {
                // PhoneNumber is AES-GCM encrypted at rest exactly like Email; this Dapper read
                // bypasses the EF converter, so without decrypting here the caller would send
                // ciphertext to whatever addresses the user by phone.
                result[row.UserId] = decryptor.Decrypt(row.PhoneNumber);
            }
        }

        return result;
    }

    /// <summary>
    /// All user Ids in a tenant — membership only, deliberately NO IsActive/soft-delete filter so
    /// the set equals a raw <c>WHERE TenantId = @tenant</c>. Lets a cross-module caller tenant-bind
    /// its own user-referencing rows (e.g. audit.LoginAttempts) without a cross-schema subquery.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> GetUserIdsInTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT Id
            FROM [identity].AspNetUsers
            WHERE TenantId = @TenantId
            """;

        var parameters = new DynamicParameters();
        parameters.Add("TenantId", tenantId);

        var rows = await db.QueryAsync<Guid>(sql, parameters, ct);
        return rows.ToList();
    }

    /// <summary>
    /// Resolve effective permission codes via IEffectivePermissionService (Redis-cached, TTL 10min).
    /// Returns empty set if user has no permissions (or does not exist — contract does not leak).
    /// </summary>
    public async Task<IReadOnlySet<string>> GetEffectivePermissionCodesAsync(
        Guid userId, CancellationToken ct = default)
    {
        var summary = await permissionService.GetSummaryAsync(userId, ct);
        return summary.PermissionCodes;
    }

    /// <summary>
    /// LIVE (no-cache) active/lockout read (RT2-4). Direct Dapper query — deliberately
    /// bypasses the permission cache: the override-target gate must reflect a just-deactivated
    /// or just-locked account. IsLockedOut follows ASP.NET Identity semantics
    /// (LockoutEnabled AND LockoutEnd in the future). Missing user → inactive (no existence leak).
    /// </summary>
    public async Task<UserActiveStatusDto> GetUserActiveStatusAsync(
        Guid userId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT TOP 1
                IsActive,
                CAST(CASE WHEN LockoutEnabled = 1
                           AND LockoutEnd IS NOT NULL
                           AND LockoutEnd > SYSDATETIMEOFFSET()
                          THEN 1 ELSE 0 END AS bit) AS IsLockedOut
            FROM [identity].AspNetUsers
            WHERE Id = @UserId
            """;

        var parameters = new DynamicParameters();
        parameters.Add("UserId", userId);

        var row = await db.QueryFirstOrDefaultAsync<UserActiveStatusDto>(sql, parameters, ct);
        return row ?? new UserActiveStatusDto { IsActive = false, IsLockedOut = false };
    }

    /// <summary>
    /// Direct-only role-name lookup for Workflow ByRole predicate-pool matching
    /// (T3-WF-CANDIDATEINDEX-READSIDE-EXTENSION Phase 01).
    /// PQ-1 A semantic: <c>AspNetUserRoles</c> ONLY — explicitly NOT
    /// <see cref="IEffectivePermissionService"/> (which merges group-inherited roles via
    /// <c>UserGroupMembership → GroupRoleAssignment</c> and would silently leak them).
    /// Canonical UPPER+TRIM matches <c>WorkflowGraphValidator</c> Save-time canonicalization
    /// (codes are stable ASCII, e.g. "BTC_MINISTER" — mirrors Permission/Group/OrgUnit code semantics).
    /// </summary>
    public async Task<IReadOnlySet<string>> GetUserRoleCodesAsync(
        Guid userId, Guid tenantId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT DISTINCT UPPER(LTRIM(RTRIM(r.Code))) AS Code
            FROM [identity].AspNetUserRoles ur
            INNER JOIN [identity].AspNetRoles r ON r.Id = ur.RoleId
            WHERE ur.UserId = @UserId
              AND r.IsActive = 1
              AND (r.TenantId = @TenantId OR r.TenantId IS NULL)
            """;

        var parameters = new DynamicParameters();
        parameters.Add("UserId", userId);
        parameters.Add("TenantId", tenantId);

        var rows = await db.QueryAsync<string>(sql, parameters, ct);
        return new HashSet<string>(rows, StringComparer.Ordinal);
    }

    /// <summary>
    /// Direct-membership group-code lookup for Workflow ByGroup predicate-pool matching
    /// (T3-WF-CANDIDATEINDEX-READSIDE-EXT-GROUP — mirror of ByRole READSIDE-EXTENSION).
    /// UserGroupMembership has NO IsActive column (presence = membership); filters active
    /// group + active user + (group-tenant = caller-tenant OR group-tenant IS NULL).
    /// Canonical UPPER+TRIM matches <c>WorkflowGraphValidator</c> Save-time canonicalization.
    /// Direct Dapper read — NOT cached.
    /// </summary>
    public async Task<IReadOnlySet<string>> GetUserGroupCodesAsync(
        Guid userId, Guid tenantId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT DISTINCT UPPER(LTRIM(RTRIM(g.Code))) AS Code
            FROM [identity].[UserGroupMemberships] m
            INNER JOIN [identity].[UserGroups] g ON g.Id = m.GroupId
            INNER JOIN [identity].AspNetUsers u ON u.Id = m.UserId
            WHERE m.UserId = @UserId
              AND g.IsActive = 1
              AND u.IsActive = 1
              AND (g.TenantId = @TenantId OR g.TenantId IS NULL)
            """;

        var parameters = new DynamicParameters();
        parameters.Add("UserId", userId);
        parameters.Add("TenantId", tenantId);

        var rows = await db.QueryAsync<string>(sql, parameters, ct);
        return new HashSet<string>(rows, StringComparer.Ordinal);
    }

    /// <summary>
    /// RT2-15 Phase 02 RUNTIME resolver — 3-table JOIN returning DISTINCT active user IDs.
    /// Filters: UserGroup.IsActive=1 AND ApplicationUser.IsActive=1 AND group Code IN @Codes
    ///          AND (UserGroup.TenantId = @TenantId OR UserGroup.TenantId IS NULL).
    /// The NULL-TenantId clause supports global groups (shared across tenants).
    /// No per-request cache (E-Q1 spec). Single roundtrip, NoTracking equivalent via Dapper.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> ResolveActiveUsersInGroupsAsync(
        IReadOnlyList<string> groupCodes,
        Guid tenantId,
        CancellationToken ct = default)
    {
        if (groupCodes.Count == 0) return Array.Empty<Guid>();

        const string sql = """
            SELECT DISTINCT m.UserId
            FROM [identity].[UserGroupMemberships] m
            INNER JOIN [identity].[UserGroups] g ON g.Id = m.GroupId
            INNER JOIN [identity].AspNetUsers u ON u.Id = m.UserId
            WHERE g.Code IN @Codes
              AND g.IsActive = 1
              AND u.IsActive = 1
              AND (g.TenantId = @TenantId OR g.TenantId IS NULL)
            """;

        var parameters = new DynamicParameters();
        parameters.Add("Codes", groupCodes);
        parameters.Add("TenantId", tenantId);

        var rows = await db.QueryAsync<Guid>(sql, parameters, ct);
        return rows.ToList();
    }

    /// <summary>
    /// Reverse role→users resolver — DIRECT AspNetUserRoles only, the exact inverse of
    /// <see cref="GetUserRoleCodesAsync"/> (UPPER+TRIM code match, active role, tenant-or-null).
    /// Deliberately excludes group-inherited roles + delegation so the result equals the set
    /// that can actually claim a ByRole pool task. Adds u.IsActive=1 to drop disabled accounts.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> ResolveActiveUsersInRolesAsync(
        IReadOnlyList<string> roleCodes,
        Guid tenantId,
        CancellationToken ct = default)
    {
        if (roleCodes.Count == 0) return Array.Empty<Guid>();

        const string sql = """
            SELECT DISTINCT ur.UserId
            FROM [identity].AspNetUserRoles ur
            INNER JOIN [identity].AspNetRoles r ON r.Id = ur.RoleId
            INNER JOIN [identity].AspNetUsers u ON u.Id = ur.UserId
            WHERE UPPER(LTRIM(RTRIM(r.Code))) IN @RoleCodes
              AND r.IsActive = 1
              AND u.IsActive = 1
              AND (r.TenantId = @TenantId OR r.TenantId IS NULL)
            """;

        var parameters = new DynamicParameters();
        parameters.Add("RoleCodes", roleCodes);
        parameters.Add("TenantId", tenantId);

        var rows = await db.QueryAsync<Guid>(sql, parameters, ct);
        return rows.ToList();
    }

    /// <summary>
    /// RT2-15 Phase 02 SAVE-VALIDATION Q2 — returns the SUBSET of input codes that match an active UserGroup
    /// visible under the caller's tenant. Scoped to: (TenantId = @TenantId OR TenantId IS NULL).
    /// Caller computes set-difference (input \ returned = unknown) to detect typo/cross-tenant group codes at Save-time.
    /// Single indexed query: WHERE Code IN @Codes AND IsActive=1 AND (TenantId=@TenantId OR TenantId IS NULL).
    /// </summary>
    public async Task<IReadOnlySet<string>> ResolveActiveUserGroupsByCodesAsync(
        IReadOnlyCollection<string> codes,
        Guid tenantId,
        CancellationToken ct = default)
    {
        if (codes.Count == 0) return new HashSet<string>(StringComparer.Ordinal);

        const string sql = """
            SELECT DISTINCT Code
            FROM [identity].[UserGroups]
            WHERE Code IN @Codes
              AND IsActive = 1
              AND (TenantId = @TenantId OR TenantId IS NULL)
            """;

        var parameters = new DynamicParameters();
        parameters.Add("Codes", codes);
        parameters.Add("TenantId", tenantId);

        var rows = await db.QueryAsync<string>(sql, parameters, ct);
        return new HashSet<string>(rows, StringComparer.Ordinal);
    }
}
