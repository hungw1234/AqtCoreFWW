namespace AqtCoreFW.SharedKernel.Contracts.Clients;

/// <summary>
/// Cross-module interface for Identity lookups.
/// Monolith: InProcessIdentityModuleClient (direct DB query, zero overhead).
/// Microservice: GrpcIdentityModuleClient (when module extracted — Phase 2+).
/// </summary>
public interface IIdentityModuleClient
{
    /// <summary>Max IDs per batch call. Exceeding throws ArgumentException.</summary>
    const int MaxBatchSize = 5000;

    /// <summary>
    /// Resolve user by email. Returns null if not found.
    /// [RT-05/VAL-01] Modified from UserLookupResult to UserInfoDto for richer data.
    /// </summary>
    /// <param name="email">Email to search</param>
    /// <param name="tenantId">Optional tenant filter for data isolation</param>
    Task<UserInfoDto?> FindByEmailAsync(string email, Guid? tenantId = null, CancellationToken ct = default);

    /// <summary>
    /// Batch lookup user info by IDs. Returns only found users (missing IDs omitted).
    /// Chunked at 500 IDs internally. Max 5000 IDs per call.
    /// </summary>
    /// <param name="userIds">User IDs to look up</param>
    /// <param name="tenantId">Optional tenant filter for data isolation [RT-02]</param>
    Task<IReadOnlyDictionary<Guid, UserInfoDto>> GetUserInfoByIdsAsync(
        IEnumerable<Guid> userIds, Guid? tenantId = null, CancellationToken ct = default);

    /// <summary>
    /// Batch lookup of recipients' phone numbers, decrypted. Same batching, chunking and tenant
    /// filter as <see cref="GetUserInfoByIdsAsync"/>.
    /// <para>
    /// Deliberately NOT a field on <see cref="UserInfoDto"/>: the phone number is Confidential
    /// (QĐ 742 §66), whereas <see cref="GetUserInfoByIdsAsync"/> feeds display-name enrichment across
    /// many read paths including SignalR broadcasts. A separate method keeps the number out of every
    /// caller that has no use for it, so only the dispatch path that addresses a user by phone holds it.
    /// </para>
    /// <para>
    /// A user that exists but has no number maps to <c>null</c>; a user that does not exist is absent
    /// from the dictionary. Callers must distinguish the two — they are different failures.
    /// </para>
    /// <para>
    /// <paramref name="tenantId"/> is REQUIRED here, unlike on the sibling lookups where it is an
    /// optional filter. An unscoped call would return phone numbers for any user id in any tenant;
    /// for a display name that is a tolerable convenience, for Confidential PII it is a cross-tenant
    /// leak one omitted argument away. No unscoped overload is offered, by design.
    /// </para>
    /// </summary>
    /// <param name="userIds">User IDs to look up</param>
    /// <param name="tenantId">Tenant the recipients must belong to — mandatory, see remarks</param>
    Task<IReadOnlyDictionary<Guid, string?>> GetUserPhoneNumbersByIdsAsync(
        IEnumerable<Guid> userIds, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Returns the Ids of every user belonging to the given tenant. Used by other modules
    /// to tenant-bind their own rows that reference a user but carry no TenantId column of
    /// their own (e.g. audit.LoginAttempts) without reaching into the identity schema directly.
    /// <para>
    /// Membership only: NO IsActive / soft-delete filter — the result must equal the set a raw
    /// <c>WHERE AspNetUsers.TenantId = @tenant</c> would yield, so callers that swap a cross-schema
    /// subquery for this call see identical rows. Returns empty when the tenant has no users.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<Guid>> GetUserIdsInTenantAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Resolve the effective permission codes for a user (merged from direct roles,
    /// group roles, and active delegations). Returns empty set if user has none.
    /// Backed by IEffectivePermissionService (10-min Redis cache — callers accept staleness).
    /// Used by cross-module authz (e.g. Workflow transition permission checks).
    /// </summary>
    Task<IReadOnlySet<string>> GetEffectivePermissionCodesAsync(
        Guid userId, CancellationToken ct = default);

    /// <summary>
    /// LIVE (no-cache) active/lockout status for a single user. Unlike
    /// <see cref="GetEffectivePermissionCodesAsync"/> (10-min cached, staleness-tolerant),
    /// this is a direct DB read — used by the Workflow assignor-override path (RT2-4) where
    /// granting a stale-active/locked-out user the task is a security defect. A missing user
    /// resolves to inactive (contract does not leak existence): <c>IsActive=false</c>.
    /// </summary>
    Task<UserActiveStatusDto> GetUserActiveStatusAsync(
        Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Returns the canonical (UPPER+TRIM) codes of roles the user has via direct
    /// <c>AspNetUserRoles</c> entries (<c>AspNetRoles.Code</c>, e.g. "BTC_MINISTER" — stable
    /// ASCII, mirrors the Permission/Group/OrgUnit code semantics). DOES NOT include group-inherited roles via
    /// <c>UserGroupMembership → GroupRoleAssignment</c> — semantic locked by PQ-1 A
    /// direct-only (RT2-15 Phase 03). Filtered by <c>AspNetRoles.IsActive = true</c> AND
    /// <c>(TenantId = @TenantId OR TenantId IS NULL)</c> (global roles).
    /// <para>
    /// Used by Workflow claim/inbox queries to match <c>WorkflowTaskCandidateIndex</c> rows
    /// where <c>PrincipalType = Role</c> (T3-WF-CANDIDATEINDEX-READSIDE-EXTENSION).
    /// </para>
    /// <para>
    /// Direct Dapper read — NOT cached (unlike <see cref="GetEffectivePermissionCodesAsync"/>).
    /// Each call reflects current role assignment. Cache as P4 follow-up if perf demands.
    /// MUST NOT delegate to <c>IEffectivePermissionService</c> — that merges group-inherited
    /// roles and would silently breach PQ-1 A.
    /// </para>
    /// </summary>
    Task<IReadOnlySet<string>> GetUserRoleCodesAsync(
        Guid userId, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Returns the canonical (UPPER+TRIM) codes of active <c>UserGroup</c> rows the user belongs
    /// to via <c>UserGroupMembership</c>. Filtered by <c>UserGroup.IsActive = true</c> AND
    /// <c>ApplicationUser.IsActive = true</c> AND <c>(UserGroup.TenantId = @TenantId OR
    /// UserGroup.TenantId IS NULL)</c> (global groups). UserGroupMembership has NO IsActive
    /// column (presence = active).
    /// <para>
    /// Used by Workflow claim/inbox queries to match <c>WorkflowTaskCandidateIndex</c> rows
    /// where <c>PrincipalType = Group</c> (T3-WF-CANDIDATEINDEX-READSIDE-EXT-GROUP — mirror
    /// of ByRole READSIDE-EXTENSION). Direct Dapper read — NOT cached. Each call reflects
    /// current group membership.
    /// </para>
    /// </summary>
    Task<IReadOnlySet<string>> GetUserGroupCodesAsync(
        Guid userId, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// RT2-15 Phase 02 RUNTIME resolver — returns active-user IDs across active UserGroups by Code.
    /// Filter: UserGroup.IsActive=true AND ApplicationUser.IsActive=true AND
    /// (UserGroup.TenantId = <paramref name="tenantId"/> OR UserGroup.TenantId IS NULL).
    /// The NULL-TenantId escape hatch supports global groups shared across tenants.
    /// UserGroupMembership has NO IsActive column (presence = active).
    /// Codes case-canonical (caller responsibility — validator canonicalizes Trim+ToUpperInvariant Save-time).
    /// Returns DISTINCT user ids (a user in multiple groups appears once).
    /// </summary>
    Task<IReadOnlyList<Guid>> ResolveActiveUsersInGroupsAsync(
        IReadOnlyList<string> groupCodes,
        Guid tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// Reverse of <see cref="GetUserRoleCodesAsync"/> — returns active-user IDs holding any of the
    /// given roles by canonical code. DIRECT AspNetUserRoles only (no group-inherited / delegation),
    /// mirroring the forward ByRole claim-eligibility semantics so the displayed "who can claim" set
    /// equals the set that can actually claim. Filter: AspNetRole.IsActive=1 AND AspNetUser.IsActive=1
    /// AND (role.TenantId = <paramref name="tenantId"/> OR role.TenantId IS NULL). Role codes must be
    /// canonical (UPPER+TRIM) — same form the candidate snapshot stores. Returns DISTINCT user ids.
    /// </summary>
    Task<IReadOnlyList<Guid>> ResolveActiveUsersInRolesAsync(
        IReadOnlyList<string> roleCodes,
        Guid tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// RT2-15 Phase 02 SAVE-VALIDATION Q2 — returns SUBSET of input codes that resolve to an ACTIVE UserGroup
    /// visible under the caller's tenant. Scoped to: (TenantId = <paramref name="tenantId"/> OR TenantId IS NULL)
    /// to mirror the <c>(Code, TenantId)</c> UNIQUE-index semantics and global-group escape hatch.
    /// Caller (WorkflowGraphValidator) computes set-difference (input \ returned = unknown) and throws
    /// GraphValidationFailed with UnknownGroupCodes metadata key when non-empty.
    /// Latency target: p99 &lt; 200ms (single indexed WHERE Code IN @codes).
    /// </summary>
    Task<IReadOnlySet<string>> ResolveActiveUserGroupsByCodesAsync(
        IReadOnlyCollection<string> codes,
        Guid tenantId,
        CancellationToken ct = default);
}

/// <summary>
/// [RT-12] Class-based DTO (positional records fail Dapper materialization).
/// Live override-target gate (RT2-4): a user is eligible only when active AND not locked out.
/// </summary>
public sealed class UserActiveStatusDto
{
    public bool IsActive { get; init; }
    public bool IsLockedOut { get; init; }
}

/// <summary>
/// [RT-12] Class-based DTO — positional records fail Dapper materialization with nullable params.
/// Used for cross-module user info enrichment.
/// </summary>
public sealed class UserInfoDto
{
    public Guid UserId { get; init; }
    public string? Email { get; init; }
    public string? FullName { get; init; }
    public Guid? TenantId { get; init; }
    public Guid? PrimaryOrgUnitId { get; init; }
}
