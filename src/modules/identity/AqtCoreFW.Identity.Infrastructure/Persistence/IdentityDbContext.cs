using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence.Configurations;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Infrastructure.Persistence;

/// <summary>
/// Identity module DbContext — schema "identity", separate from other module contexts.
/// Does NOT extend ModuleDbContext (Identity has its own base via IdentityDbContext&lt;T&gt;).
/// OpenIddict tables are registered here via UseOpenIddict().
///
/// IKeyResolver is injected so ApplicationUserConfiguration can wire EncryptedStringConverter
/// for ApplicationUser.TotpSecret with versioned keys (v{N}: prefix, dual-decode per D10.2).
/// Parameterless ctor (for design-time migration tool) omits encryption.
/// </summary>
public class IdentityDbContext
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    private readonly IKeyResolver? _keyResolver;

    /// <summary>Design-time migration scaffolding ctor — no encryption key.</summary>
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options) : base(options) { }

    /// <summary>Runtime ctor — wires versioned encryption for TotpSecret.</summary>
    public IdentityDbContext(
        DbContextOptions<IdentityDbContext> options,
        IKeyResolver keyResolver)
        : base(options)
    {
        _keyResolver = keyResolver;
    }

    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserDelegation> UserDelegations => Set<UserDelegation>();
    public DbSet<AccessReviewRecord> AccessReviewRecords => Set<AccessReviewRecord>();
    public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();
    public DbSet<AttributeRule> AttributeRules => Set<AttributeRule>();
    public DbSet<UserGroup> UserGroups => Set<UserGroup>();
    public DbSet<UserGroupMembership> UserGroupMemberships => Set<UserGroupMembership>();
    public DbSet<GroupRoleAssignment> GroupRoleAssignments => Set<GroupRoleAssignment>();
    public DbSet<DataScopeType> DataScopeTypes => Set<DataScopeType>();
    public DbSet<RoleDataScope> RoleDataScopes => Set<RoleDataScope>();
    public DbSet<UserDataScopeOverride> UserDataScopeOverrides => Set<UserDataScopeOverride>();
    public DbSet<PolicyRule> PolicyRules => Set<PolicyRule>();
    public DbSet<SodConflictRule> SodConflictRules => Set<SodConflictRule>();
    public DbSet<AppMenu> AppMenus => Set<AppMenu>();
    public DbSet<MenuRolePermission> MenuRolePermissions => Set<MenuRolePermission>();
    public DbSet<ExternalIdentity> ExternalIdentities => Set<ExternalIdentity>();
    public DbSet<CredentialPolicy> CredentialPolicies => Set<CredentialPolicy>();
    public DbSet<JitProviderConfig> JitProviderConfigs => Set<JitProviderConfig>();
    public DbSet<SodViolationLog> SodViolationLogs => Set<SodViolationLog>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<MfaPolicy> MfaPolicies => Set<MfaPolicy>();
    public DbSet<MfaEmergencyDisableRequest> MfaEmergencyDisableRequests => Set<MfaEmergencyDisableRequest>();
    public DbSet<IpFilterRule> IpFilterRules => Set<IpFilterRule>();
    public DbSet<SigningDocFlowRecord> SigningDocFlowRecords => Set<SigningDocFlowRecord>();
    public DbSet<SigningDocFlowNonce> SigningDocFlowNonces => Set<SigningDocFlowNonce>();
    public DbSet<SigningPendingTransaction> SigningPendingTransactions => Set<SigningPendingTransaction>();
    public DbSet<MysignInboundToken> MysignInboundTokens => Set<MysignInboundToken>();
    public DbSet<SignerCertEnrollment> SignerCertEnrollments => Set<SignerCertEnrollment>();
    public DbSet<SignerEnrollmentNonce> SignerEnrollmentNonces => Set<SignerEnrollmentNonce>();
    public DbSet<UserSignatureProfile> UserSignatureProfiles => Set<UserSignatureProfile>();
    public DbSet<SigningRequest> SigningRequests => Set<SigningRequest>();
    public DbSet<DocumentSignatureSession> DocumentSignatureSessions => Set<DocumentSignatureSession>();
    public DbSet<DocumentSignatureSessionSigner> DocumentSignatureSessionSigners => Set<DocumentSignatureSessionSigner>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        base.OnModelCreating(mb);
        mb.HasDefaultSchema("identity");

        // Assembly scan picks up all IEntityTypeConfiguration with parameterless ctors.
        // ApplicationUserConfiguration has a parameterless ctor (falls back to no encryption
        // when IKeyResolver not available — e.g. design-time migration scaffolding).
        // We override the scanned registration by applying the resolver-injected version afterwards.
        mb.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);

        // Override ApplicationUserConfiguration with the IKeyResolver-injected variant
        // so TotpSecret uses versioned encryption keys from Vault at runtime.
        mb.ApplyConfiguration(new ApplicationUserConfiguration(_keyResolver));

        // Register OpenIddict entity model so EF migrations capture the tables.
        mb.UseOpenIddict();
    }
}
