using System.Reflection;
using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Infrastructure.Persistence;
using FluentAssertions;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// Compliance evidence tests for QÄ 742 Phase-04 (mục 4.4.b/c/d — Data Classification).
///
/// Verifies Phase-04 Wave-4 deliverables structurally (no runtime SQL or Vault dependency):
///   - PlaintextToEncryptedUpgrader exists with PkColumn override (Wave-4 fix)
///   - EmailHashBackfillService exists with BackfillAsync (Wave-3)
///   - RtbfRequestConfiguration has dual-ctor pattern (Wave-4)
///   - AuditDbContext has dual-ctor pattern (Wave-4)
///   - EncryptRtbfPiiColumns migration exists (Wave-4)
/// </summary>
public sealed class Qd742Phase04DataClassificationComplianceTests
{
    // ── Wave-4: RtbfRequest PII encryption structural guards ─────────────────

    [Fact]
    public void Compliance_4_4_Wave4_PlaintextToEncryptedUpgrader_ExistsWithPkColumnOverride()
    {
        // QĐ 742 §4.4 Wave-4: PlaintextToEncryptedUpgrader MUST exist for the
        // plaintext→encrypted startup migration path (distinct from LegacyCipherUpgrader).
        var upgraderType = typeof(PlaintextToEncryptedUpgrader);
        upgraderType.Should().NotBeNull();

        var method = upgraderType.GetMethod(
            nameof(PlaintextToEncryptedUpgrader.EncryptColumnAsync),
            BindingFlags.Public | BindingFlags.Static);
        method.Should().NotBeNull("EncryptColumnAsync must be public static");

        // ColumnTarget MUST have a PkColumn property with a default value of "Id".
        // Regression guard: the original implementation hardcoded 'Id' which crashed for
        // audit.RtbfRequests (PK = InternalId). PkColumn was added as a fix.
        var columnTargetType = upgraderType.GetNestedType("ColumnTarget");
        columnTargetType.Should().NotBeNull("ColumnTarget nested record must exist");

        var pkColumnProp = columnTargetType!.GetProperty("PkColumn");
        pkColumnProp.Should().NotBeNull(
            "ColumnTarget.PkColumn must exist — it is the fix for the InternalId vs Id bug");
    }

    [Fact]
    public void Compliance_4_4_Wave4_EmailHashBackfillService_ExistsWithBackfillAsync()
    {
        // QĐ 742 §4.4 Wave-3: EmailHashBackfillService MUST exist to populate
        // NormalizedEmailHashGlobal/EmailHashGlobal before app.Run() so login works post-encryption.
        // Use typeof() directly — arch tests project-reference Identity.Infrastructure,
        // ensuring the assembly is loaded and the check survives renames.
        var serviceType = typeof(AqtCoreFW.Identity.Infrastructure.Services.EmailHashBackfillService);
        serviceType.Should().NotBeNull("EmailHashBackfillService must exist in Identity.Infrastructure");

        var backfillMethod = serviceType.GetMethod(
            "BackfillAsync",
            BindingFlags.Public | BindingFlags.Static);
        backfillMethod.Should().NotBeNull("BackfillAsync must be public static");
        backfillMethod!.ReturnType.Name.Should().StartWith("Task",
            because: "BackfillAsync must return a Task-based type");
    }

    [Fact]
    public void Compliance_4_4_Wave4_RtbfRequestConfiguration_HasDualCtorPattern()
    {
        // AuditDbContext injects IKeyResolver into RtbfRequestConfiguration for runtime encryption.
        // The dual-ctor pattern (parameterless + IKeyResolver?) MUST be present:
        //   - Parameterless: design-time migration scaffolding (no encryption)
        //   - IKeyResolver?: runtime encryption wiring
        var configType = typeof(AqtCoreFW.Audit.Infrastructure.Persistence.Configurations.RtbfRequestConfiguration);
        configType.Should().NotBeNull();

        var parameterlessCtor = configType.GetConstructor(Type.EmptyTypes);
        parameterlessCtor.Should().NotBeNull(
            "RtbfRequestConfiguration must have a parameterless ctor for design-time migration");

        var resolverCtor = configType.GetConstructor(
            new[] { typeof(IKeyResolver) });
        resolverCtor.Should().NotBeNull(
            "RtbfRequestConfiguration must have an IKeyResolver ctor for runtime encryption wiring");
    }

    [Fact]
    public void Compliance_4_4_Wave4_AuditDbContext_HasDualCtorPattern()
    {
        // AuditDbContext MUST follow the same dual-ctor pattern as IdentityDbContext so EF DI
        // auto-selects the richer ctor when IKeyResolver is registered in the DI container.
        var dbContextType = typeof(AqtCoreFW.Audit.Infrastructure.Persistence.AuditDbContext);
        dbContextType.Should().NotBeNull();

        var parameterlessCtor = dbContextType.GetConstructor(
            new[] { typeof(Microsoft.EntityFrameworkCore.DbContextOptions<AqtCoreFW.Audit.Infrastructure.Persistence.AuditDbContext>) });
        parameterlessCtor.Should().NotBeNull(
            "AuditDbContext must have a (DbContextOptions) ctor for design-time migration");

        var resolverCtor = dbContextType.GetConstructor(
            new[]
            {
                typeof(Microsoft.EntityFrameworkCore.DbContextOptions<AqtCoreFW.Audit.Infrastructure.Persistence.AuditDbContext>),
                typeof(IKeyResolver)
            });
        resolverCtor.Should().NotBeNull(
            "AuditDbContext must have a (DbContextOptions, IKeyResolver) ctor — EF selects this at runtime");
    }

    [Fact]
    public void Compliance_4_4_Wave4_EncryptRtbfPiiColumns_MigrationExists()
    {
        // The column-widening migration MUST exist at the committed path.
        // Without this migration, nvarchar(256)/nvarchar(12) overflow on first encrypted insert.
        var migrationPath = Path.Combine(
            GetRepoRoot(),
            "src", "modules", "audit",
            "AqtCoreFW.Audit.Infrastructure",
            "Persistence", "Migrations",
            "20260510120821_EncryptRtbfPiiColumns.cs");

        File.Exists(migrationPath).Should().BeTrue(
            "EncryptRtbfPiiColumns migration must exist — SubjectEmail 256→1024, CitizenNationalId 12→256");

        var content = File.ReadAllText(migrationPath);
        content.Should().Contain("nvarchar(1024)", because: "migration must widen SubjectEmail to nvarchar(1024)");
        content.Should().Contain("nvarchar(256)", because: "migration must widen CitizenNationalId to nvarchar(256)");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string GetRepoRoot()
    {
        // Anchor by .git directory (always at repo root, format-agnostic).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException("Repo root (.git) not found from " + AppContext.BaseDirectory);
    }
}
