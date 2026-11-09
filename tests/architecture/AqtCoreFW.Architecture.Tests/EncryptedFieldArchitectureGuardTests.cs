using System.Reflection;
using AqtCoreFW.Audit.Infrastructure.Persistence;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Identity.Infrastructure.Persistence.Configurations;
using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Compliance;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.SqlServer;
using Xunit;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// D11.5 Architecture guard: every property marked [DataClassification(Restricted)]
/// MUST either:
///   (a) carry [DoNotEncryptAtRest] with an explicit reason, OR
///   (b) have EncryptedStringConverter applied via EF model metadata in its owning DbContext.
///
/// Scope: IdentityDbContext + AuditDbContext — the two contexts that have IKeyResolver wiring.
/// Other DbContexts (WebhookDbContext, etc.) are not scoped for field-level encryption
/// and must annotate Restricted props with [DoNotEncryptAtRest].
///
/// Why this guard exists:
///   Fixup P1-1 showed that ApplicationUser.PhoneNumber was listed as Confidential in the plan
///   but the encryption converter was never wired. This test would have caught that gap
///   immediately by failing on first compile + test run.
/// </summary>
public sealed class EncryptedFieldArchitectureGuardTests
{
    // Fake connection string — EF builds the model without connecting to DB.
    private const string FakeConnStr = "Server=.;Database=arch_guard_test;Trusted_Connection=True;";

    // ── Guard: all [Restricted] props in IdentityDbContext must be encrypted or annotated ─────

    [Fact]
    public void All_Restricted_Identity_Properties_Have_EncryptedConverter_Or_DoNotEncryptAtRest()
    {
        // Arrange: build IdentityDbContext model with IKeyResolver stub so converters are wired.
        // UseSqlServer builds the full model without a live connection (design-time safe).
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlServer(FakeConnStr)
            .Options;

        // Use a non-null key resolver so ApplicationUserConfiguration wires converters.
        var keyResolver = new ArchGuardKeyResolver();
        using var ctx = new IdentityDbContext(options, keyResolver);

        var violations = FindViolations(ctx);

        violations.Should().BeEmpty(
            because: "Every [DataClassification(Restricted)] property in IdentityDbContext must have " +
                     "EncryptedStringConverter applied via EF configuration OR carry [DoNotEncryptAtRest]. " +
                     "Violations: {0}", string.Join(", ", violations));
    }

    [Fact]
    public void All_Restricted_Audit_Properties_Have_EncryptedConverter_Or_DoNotEncryptAtRest()
    {
        // Arrange: build AuditDbContext model with IKeyResolver stub.
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlServer(FakeConnStr)
            .Options;

        var keyResolver = new ArchGuardKeyResolver();
        using var ctx = new AuditDbContext(options, keyResolver);

        var violations = FindViolations(ctx);

        violations.Should().BeEmpty(
            because: "Every [DataClassification(Restricted)] property in AuditDbContext must have " +
                     "EncryptedStringConverter applied via EF configuration OR carry [DoNotEncryptAtRest]. " +
                     "Violations: {0}", string.Join(", ", violations));
    }

    // ── Invariant: [DoNotEncryptAtRest] annotations have non-empty reasons ────────────────

    [Fact]
    public void DoNotEncryptAtRest_Annotations_Must_Have_NonEmpty_Reason()
    {
        // Scan all assemblies in the solution for [DoNotEncryptAtRest] usage.
        var domainAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.FullName?.StartsWith("AqtCoreFW", StringComparison.Ordinal) == true);

        var violations = new List<string>();

        foreach (var asm in domainAssemblies)
        {
            foreach (var type in TryGetTypes(asm))
            {
                foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    var attr = prop.GetCustomAttribute<DoNotEncryptAtRestAttribute>();
                    if (attr is null) continue;
                    if (string.IsNullOrWhiteSpace(attr.Reason))
                        violations.Add($"{type.Name}.{prop.Name}: [DoNotEncryptAtRest] reason is empty");
                }
            }
        }

        violations.Should().BeEmpty(
            because: "[DoNotEncryptAtRest] MUST document the justification for future maintainers. " +
                     "Empty reasons: {0}", string.Join("; ", violations));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// For each entity type in the DbContext model:
    ///   1. Find CLR properties with [DataClassification(Restricted)]
    ///   2. If [DoNotEncryptAtRest] is present → skip (opt-out documented)
    ///   3. Else → assert EF model property has a value converter starting with "EncryptedStringConverter"
    /// Returns violation description strings (empty = all good).
    /// </summary>
    private static List<string> FindViolations(DbContext ctx)
    {
        var violations = new List<string>();
        var model = ctx.Model;

        foreach (var entityType in model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;
            if (clrType is null) continue;

            foreach (var clrProp in clrType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                // Check for [DataClassification(Restricted)]
                var classAttr = clrProp.GetCustomAttribute<DataClassificationAttribute>();
                if (classAttr?.Level != DataClassificationLevel.Restricted)
                    continue;

                // [DoNotEncryptAtRest] is an explicit opt-out — skip with documented reason
                var doNotEncrypt = clrProp.GetCustomAttribute<DoNotEncryptAtRestAttribute>();
                if (doNotEncrypt is not null)
                    continue;

                // Property must have EncryptedStringConverter in EF model
                var efProp = entityType.FindProperty(clrProp.Name);
                if (efProp is null)
                {
                    // Restricted prop not mapped by EF (e.g. calculated) — skip
                    continue;
                }

                var converter = efProp.GetValueConverter();
                if (converter is null || !converter.GetType().Name.StartsWith("EncryptedStringConverter", StringComparison.Ordinal))
                {
                    violations.Add(
                        $"{clrType.Name}.{clrProp.Name}: [Restricted] but no EncryptedStringConverter in EF model " +
                        "(add converter in configuration OR annotate [DoNotEncryptAtRest(\"reason\")])");
                }
            }
        }

        return violations;
    }

    private static IEnumerable<Type> TryGetTypes(Assembly asm)
    {
        try { return asm.GetTypes(); }
        catch { return []; }
    }

    /// <summary>
    /// Minimal IKeyResolver stub for architecture guard — returns a fixed 32-byte key.
    /// Never used to encrypt/decrypt real data; only needed to satisfy the ctor contract
    /// so ApplicationUserConfiguration and RtbfRequestConfiguration wire their converters.
    /// </summary>
    private sealed class ArchGuardKeyResolver : IKeyResolver
    {
        private static readonly byte[] FixedKey = Enumerable.Repeat((byte)0x42, 32).ToArray();
        public int CurrentVersion => 1;
        public byte[] GetKey(int version) => FixedKey;
    }
}
