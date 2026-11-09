using AqtCoreFW.Api.Controllers.Admin;
using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Presentation.Controllers;
using AqtCoreFW.SharedKernel.Authorization;
using FluentAssertions;
using System.Reflection;

namespace AqtCoreFW.Architecture.Tests.Compliance;

/// <summary>
/// Compliance evidence tests for QĐ 742 Phase-05b (Mục 5.1 — chữ ký số CA integration).
/// Verifies structural requirements:
///   - TenantSignatureConfig entity exists with required fields
///   - IDigitalSignatureProvider abstraction + capability enum exist (D6 provider-agnostic mandate)
///   - SignatureConfigAdminController + DigitalSignatureController exist with correct attributes
///   - Permission codes for signature config exist in PermCodes.Security
///   - D6 gate: no hardcoded provider keys in core (Domain/Application) assemblies
///   - D6 gate: no hardcoded provider keys in non-signing Infrastructure or Api controllers
///
/// These are structural verification tests (reflection + source-text scan), not behavioral tests.
/// Behavioral tests are in SigningHandlerTests, SignatureProviderResolverTests, ProviderExtensibilityTests.
/// E2E HTTP boundary tests are in SignatureConfigApiTests, DigitalSignatureApiTests (Integration suite).
/// </summary>
public sealed class Qd742Phase05bSigningComplianceTests
{
    // ── TenantSignatureConfig Entity (QĐ 742 §5.1) ───────────────────────────────

    [Fact]
    public void Compliance_5_1_TenantSignatureConfig_EntityExists()
    {
        // QĐ 742 §5.1: Per-tenant digital signature configuration entity.
        // Evidence: TenantSignatureConfig entity type exists.

        var entityType = typeof(TenantSignatureConfig);
        entityType.IsClass.Should().BeTrue();
    }

    [Fact]
    public void Compliance_5_1_TenantSignatureConfig_HasRequiredFields()
    {
        // QĐ 742 §5.1: Config must capture provider key, API credentials ref, OCSP mode, enabled state.
        // Evidence: TenantSignatureConfig has ProviderKey, ApiCredentialsRef, OcspMode, IsEnabled properties.

        var entityType = typeof(TenantSignatureConfig);

        entityType.GetProperty("TenantId").Should().NotBeNull("TenantSignatureConfig must have TenantId");
        entityType.GetProperty("ProviderKey").Should().NotBeNull("ProviderKey identifies the VN CA vendor");
        entityType.GetProperty("ApiCredentialsRef").Should().NotBeNull("ApiCredentialsRef = Vault secret reference");
        entityType.GetProperty("OcspMode").Should().NotBeNull("OcspMode: Soft (default) or Hard per D-V3");
        entityType.GetProperty("IsEnabled").Should().NotBeNull("IsEnabled: tenant-admin toggle for signing");
        entityType.GetProperty("AllowedCaRootThumbprints").Should().NotBeNull("CA root thumbprints for chain validation");
    }

    [Fact]
    public void Compliance_5_1_TenantSignatureConfig_IsAuditableEntity()
    {
        // QĐ 742 §5.1: Config changes must be audited.
        // Evidence: TenantSignatureConfig inherits from AuditableEntity.

        var entityType = typeof(TenantSignatureConfig);
        var baseType = entityType.BaseType;

        baseType!.Name.Should().Be("AuditableEntity");
    }

    // ── IDigitalSignatureProvider Abstraction (D6 provider-agnostic mandate) ─────

    [Fact]
    public void Compliance_D6_IDigitalSignatureProvider_InterfaceExists()
    {
        // D6: abstraction must exist for provider-agnostic extension.
        // Evidence: IDigitalSignatureProvider interface type exists.

        var interfaceType = typeof(IDigitalSignatureProvider);
        interfaceType.IsInterface.Should().BeTrue();
    }

    [Fact]
    public void Compliance_D6_SignatureProviderCapabilities_FlagsEnumExists()
    {
        // D6: capability-aware routing requires [Flags] enum.
        // Evidence: SignatureProviderCapabilities enum exists with required flags.

        var enumType = typeof(SignatureProviderCapabilities);
        enumType.IsEnum.Should().BeTrue();
        enumType.GetCustomAttributes<FlagsAttribute>(inherit: false).Should().NotBeEmpty(
            "SignatureProviderCapabilities must be a [Flags] enum — routing depends on capability bitfield");

        var names = Enum.GetNames(enumType);
        names.Should().Contain("ServerSideSigning", "required for handler routing (VNPT SmartCA)");
        names.Should().Contain("ClientSideSigning", "required for rejection of USB token sign requests");
        names.Should().Contain("RequiresLocalAgent", "required for VGCA FE-plugin prerequisite signalling (D15)");
    }

    [Fact]
    public void Compliance_D6_OcspMode_EnumExists()
    {
        // D-V3: hybrid OCSP — Soft default + tenant-admin toggle to Hard.
        // Evidence: OcspMode enum exists with Soft and Hard values.

        var enumType = typeof(OcspMode);
        enumType.IsEnum.Should().BeTrue();

        var names = Enum.GetNames(enumType);
        names.Should().Contain("Soft", "D-V3: default mode — OCSP failure is non-blocking");
        names.Should().Contain("Hard", "D-V3: enforcement mode — OCSP failure blocks signing");
    }

    // ── SignatureConfigAdminController (QĐ 742 §5.1 admin CRUD) ─────────────────

    [Fact]
    public void Compliance_5_1_SignatureConfigAdminController_Exists()
    {
        // QĐ 742 §5.1: Admin API for per-tenant signature provider config CRUD.
        // Evidence: SignatureConfigAdminController type exists in Api.Controllers.Admin namespace.

        var controllerType = typeof(SignatureConfigAdminController);
        controllerType.IsClass.Should().BeTrue();
        controllerType.Name.Should().Be("SignatureConfigAdminController");
    }

    [Fact]
    public void Compliance_5_1_SignatureConfigAdminController_HasRequiresMfaAttribute()
    {
        // QĐ 742 §1.6.b: Admin operations must require a valid MFA session.
        // Evidence: SignatureConfigAdminController has class-level [RequiresMfa] attribute.

        var controllerType = typeof(SignatureConfigAdminController);
        var attr = controllerType.GetCustomAttribute<RequiresMfaAttribute>(inherit: false);

        attr.Should().NotBeNull("SignatureConfigAdminController must have [RequiresMfa] at class level — " +
            "all admin controllers are swept by AdminControllersMfaAttributeTests");
    }

    [Fact]
    public void Compliance_5_1_SignatureConfigAdminController_HasAuthorizeWithSignatureConfigPolicy()
    {
        // QĐ 742 §5.1: Signature config admin requires SECURITY.SIGNATURE_CONFIG.VIEW permission.
        // Evidence: SignatureConfigAdminController has [Authorize(Policy = SECURITY.SIGNATURE_CONFIG.VIEW)].

        var controllerType = typeof(SignatureConfigAdminController);
        var authorizeAttrs = controllerType.GetCustomAttributes<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>(inherit: false);

        authorizeAttrs.Should().NotBeEmpty("SignatureConfigAdminController must have [Authorize] at class level");
        authorizeAttrs.Should().Contain(a => a.Policy != null && a.Policy.Contains("SIGNATURE_CONFIG"),
            "must have [Authorize(Policy=SECURITY.SIGNATURE_CONFIG.VIEW)] at class level");
    }

    [Fact]
    public void Compliance_5_1_SignatureConfigAdminController_Has3Endpoints()
    {
        // QĐ 742 §5.1: Admin API has GET config, PUT upsert, GET available-providers endpoints.
        // Evidence: SignatureConfigAdminController has 3 public action methods.

        var controllerType = typeof(SignatureConfigAdminController);
        var publicMethods = controllerType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .ToList();

        publicMethods.Should().HaveCount(3,
            "SignatureConfigAdminController should have Get, Upsert, GetAvailableProviders");

        var methodNames = publicMethods.Select(m => m.Name).ToList();
        methodNames.Should().Contain("Get", "GET /{tenantId}");
        methodNames.Should().Contain("Upsert", "PUT /{tenantId}");
        methodNames.Should().Contain("GetAvailableProviders", "GET /available-providers");
    }

    // ── DigitalSignatureController (QĐ 742 §5.1 user-facing sign/verify) ────────

    [Fact]
    public void Compliance_5_1_DigitalSignatureController_Exists()
    {
        // QĐ 742 §5.1: User-facing digital signature endpoints.
        // Evidence: DigitalSignatureController type exists in Identity.Presentation.Controllers namespace.

        var controllerType = typeof(DigitalSignatureController);
        controllerType.IsClass.Should().BeTrue();
        controllerType.Name.Should().Be("DigitalSignatureController");
    }

    [Fact]
    public void Compliance_5_1_DigitalSignatureController_HasRequiresMfaAttribute()
    {
        // QĐ 742 §1.6.b: Signing operations must require a valid MFA session.
        // Evidence: DigitalSignatureController has class-level [RequiresMfa] attribute.

        var controllerType = typeof(DigitalSignatureController);
        var attr = controllerType.GetCustomAttribute<RequiresMfaAttribute>(inherit: false);

        attr.Should().NotBeNull("DigitalSignatureController must have [RequiresMfa] at class level — " +
            "signing/verify are security-sensitive operations per QĐ 742 §1.6.b");
    }

    [Fact]
    public void Compliance_5_1_DigitalSignatureController_HasSignAndVerifyEndpoints()
    {
        // QĐ 742 §5.1: User endpoints MUST include Sign and Verify.
        // The 3-tier signing ship (W-series) additionally exposes VerifyXml, VerifyJws and Confirm —
        // those are extra capabilities, not violations, so this guard asserts presence of the two
        // mandated endpoints rather than pinning an exact count.

        var controllerType = typeof(DigitalSignatureController);
        var methodNames = controllerType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .ToList();

        methodNames.Should().Contain("Sign", "POST /sign — server-side signing via the active CA provider");
        methodNames.Should().Contain("Verify", "POST /verify — CMS/PKCS#7 signature verification");
    }

    // ── Permission Codes (QĐ 742 §5.1 access control) ────────────────────────────

    [Fact]
    public void Compliance_5_1_PermissionCodes_SignatureConfig_ViewAndManage()
    {
        // QĐ 742 §5.1: Signature config CRUD requires explicit SystemAdmin-only permissions.
        // Evidence: PermCodes.Security.SignatureConfig.View and .Manage defined.

        var viewPermCode = PermCodes.Security.SignatureConfig.View;
        var managePermCode = PermCodes.Security.SignatureConfig.Manage;

        viewPermCode.Should().NotBeNullOrEmpty();
        managePermCode.Should().NotBeNullOrEmpty();
        viewPermCode.Should().NotBe(managePermCode);
        viewPermCode.Should().Contain("SIGNATURE_CONFIG");
        managePermCode.Should().Contain("SIGNATURE_CONFIG");
    }

    // ── Application handlers exist (QĐ 742 §5.1 MediatR pipeline) ───────────────

    [Fact]
    public void Compliance_5_1_SigningHandlers_AllExist()
    {
        // QĐ 742 §5.1: Each API endpoint dispatches via MediatR handler.
        // Evidence: All command/query handler types exist.

        typeof(SignPayloadCommand).Should().NotBeNull("sign handler command");
        typeof(VerifySignatureCommand).Should().NotBeNull("verify handler command");
        typeof(UpdateTenantSignatureConfigCommand).Should().NotBeNull("config upsert command");
        typeof(GetTenantSignatureConfigQuery).Should().NotBeNull("config get query");
        typeof(GetAvailableProvidersQuery).Should().NotBeNull("available-providers query");
    }

    // ── D6 Gate: NoHardcodedProviderKeyInCoreTests ────────────────────────────────
    //
    // D17.2 mechanism: source-text literal scan (same pattern as CrossSchemaSqlScanner).
    // Scanned scope:
    //   - Identity.Domain (all files)
    //   - Identity.Application (all files)
    //   - Identity.Infrastructure EXCLUDING Services/Signing/ (adapters legitimately contain keys)
    //   - Api host controllers EXCLUDING DigitalSignatureController.cs (contains OcspMode enum)
    //
    // Banned literals: "remote-vnpt", "usb-vgca", "fake-thirdparty"
    // Rationale: presence in core means a new provider requires a core change → violates D6.

    private static readonly string[] BannedLiterals =
    [
        "\"remote-vnpt",         // catches "remote-vnpt-smartca" + any future variant (W4: was VNPT, now Viettel)
        "\"remote-viettel",      // catches "remote-viettel-mysign" + any future variant in core layers
        "\"usb-vgca",            // catches "usb-vgca" key
        "\"fake-thirdparty",     // catches test-only fake key (should only appear in test files)
    ];

    [Fact]
    public void Compliance_D6_NoHardcodedProviderKeyInIdentityDomainAndApplication()
    {
        // D6: Domain + Application must have zero hardcoded provider keys.
        // These layers define the abstraction and handlers — they must be key-agnostic.

        var srcRoot = FindSrcRoot();

        var domainDir = Path.Combine(srcRoot, "modules", "identity", "AqtCoreFW.Identity.Domain");
        var applicationDir = Path.Combine(srcRoot, "modules", "identity", "AqtCoreFW.Identity.Application");

        var violations = new List<string>();
        ScanDirectory(domainDir, excludeSubDir: null, violations);
        ScanDirectory(applicationDir, excludeSubDir: null, violations);

        violations.Should().BeEmpty(
            "D6 mandate: Identity.Domain and Identity.Application must contain zero hardcoded " +
            "provider keys — these assemblies are the core abstraction layer. " +
            "Hardcoded keys here would require a core change per new VN CA provider. " +
            "Provider keys belong in: adapter classes (Infrastructure/Services/Signing/) " +
            "and DI registration (IdentityInfrastructureRegistration.cs).");
    }

    [Fact]
    public void Compliance_D6_NoHardcodedProviderKeyInIdentityInfrastructureOutsideSigning()
    {
        // D6: Infrastructure files OUTSIDE Services/Signing/ must not hardcode provider keys.
        // The signing adapters (Services/Signing/) legitimately contain keys — excluded from scan.
        // Repositories, DI registration boilerplate, etc. must not embed vendor strings.
        // Exception: IdentityInfrastructureRegistration.cs contains the DI key strings — excluded.

        var srcRoot = FindSrcRoot();

        var infraDir = Path.Combine(srcRoot, "modules", "identity", "AqtCoreFW.Identity.Infrastructure");
        var violations = new List<string>();

        ScanDirectory(
            infraDir,
            excludeSubDir: Path.Combine("Services", "Signing"), // adapter files — legitimate
            violations,
            excludeFile: "IdentityInfrastructureRegistration.cs");       // DI registration — legitimate

        violations.Should().BeEmpty(
            "D6 mandate: Identity.Infrastructure files OUTSIDE Services/Signing/ must not hardcode " +
            "provider keys. Repositories, context, and other infra files must stay provider-agnostic. " +
            "Adapter key strings belong only in concrete adapter classes + IdentityInfrastructureRegistration.");
    }

    [Fact]
    public void Compliance_D6_NoHardcodedProviderKeyInApiControllers_ExcludingSigningController()
    {
        // D6: Api host controllers (admin layer) must not hardcode provider keys.
        // DigitalSignatureController.cs is excluded — it contains request DTOs that might reference
        // OcspMode enum (not a provider key).

        var srcRoot = FindSrcRoot();

        var controllersDir = Path.Combine(srcRoot, "host", "AqtCoreFW.Api", "Controllers");
        Assert.True(Directory.Exists(controllersDir),
            $"Controllers directory must exist at {controllersDir} — test environment or path resolution broken");

        var violations = new List<string>();
        ScanDirectory(controllersDir, excludeSubDir: null, violations,
            excludeFile: "DigitalSignatureController.cs");

        violations.Should().BeEmpty(
            "D6 mandate: Api host controllers must not hardcode provider keys. " +
            "Controllers dispatch commands — they are provider-agnostic by design.");
    }

    // ── W4 migration gate: no VNPT symbols anywhere in src/ ──────────────────
    //
    // After W4 (Viettel Mysign replaces VNPT sp769), all Vnpt* class/type names
    // and "vnpt-smartca" string literals must be absent from src/.
    // Scans ALL src/ subtrees (including Services/Signing/ adapters) — stricter than
    // the D6 BannedLiterals scan above which intentionally excludes adapter files.
    //
    // Skips: obj/ and bin/ generated output, single-line // comments.

    [Fact]
    public void No_Vnpt_Symbol_In_Src_After_W4()
    {
        // W4 migration gate: VNPT sp769 provider has been replaced by Viettel Mysign.
        // No class names containing "Vnpt" and no "vnpt-smartca" string literals
        // should remain in any src/ .cs file (adapters included — they were renamed).

        var srcRoot = FindSrcRoot();

        var vnptSymbols = new[] { "VnptSmartCa", "vnpt-smartca", "RemoteSigningProvider" };
        var violations = new List<string>();

        foreach (var csFile in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (csFile.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                csFile.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                continue;

            var content = File.ReadAllText(csFile);
            var lines   = content.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal)) continue;

                // Strip trailing inline comment before checking
                var scanLine = lines[i];
                var inlineCommentIdx = scanLine.IndexOf("//", StringComparison.Ordinal);
                if (inlineCommentIdx >= 0)
                    scanLine = scanLine[..inlineCommentIdx];

                foreach (var sym in vnptSymbols)
                {
                    if (scanLine.Contains(sym, StringComparison.Ordinal))
                    {
                        violations.Add(
                            $"{Path.GetRelativePath(Directory.GetCurrentDirectory(), csFile)}:{i + 1}: " +
                            $"W4 migration residue '{sym}' — {lines[i].Trim()}");
                    }
                }
            }
        }

        violations.Should().BeEmpty(
            "W4 migration gate: VnptSmartCa options class, vnpt-smartca key literal, and " +
            "RemoteSigningProvider class name must be absent from all src/ files. " +
            "These were replaced by ViettelMysignOptions, remote-viettel-mysign, and ViettelMysignProvider.");
    }

    // ── Source-text scan helpers ──────────────────────────────────────────────────

    private static void ScanDirectory(
        string dir,
        string? excludeSubDir,
        List<string> violations,
        string? excludeFile = null)
    {
        if (!Directory.Exists(dir)) return;

        foreach (var csFile in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            // Skip obj/bin generated files
            if (csFile.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                csFile.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                continue;

            // Skip excluded subdirectory
            if (excludeSubDir is not null)
            {
                var fullExclude = Path.Combine(dir, excludeSubDir);
                if (csFile.StartsWith(fullExclude, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            // Skip excluded file
            if (excludeFile is not null &&
                Path.GetFileName(csFile).Equals(excludeFile, StringComparison.OrdinalIgnoreCase))
                continue;

            var content = File.ReadAllText(csFile);
            var lines = content.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                // Skip lines that are purely single-line or XML doc comments
                if (trimmed.StartsWith("//", StringComparison.Ordinal)) continue;

                // Strip trailing inline comments (// ...) before checking for banned literals.
                // Handles: `string Foo { get; }  // e.g. "remote-vnpt"` — the example is in a comment.
                var scanLine = lines[i];
                var inlineCommentIdx = scanLine.IndexOf("//", StringComparison.Ordinal);
                if (inlineCommentIdx >= 0)
                    scanLine = scanLine[..inlineCommentIdx];

                foreach (var banned in BannedLiterals)
                {
                    if (scanLine.Contains(banned, StringComparison.Ordinal))
                    {
                        violations.Add(
                            $"{Path.GetRelativePath(Directory.GetCurrentDirectory(), csFile)}:{i + 1}: " +
                            $"hardcoded provider key literal '{banned}' — {lines[i].Trim()}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Locates the repository src/ root relative to the test binary.
    /// Test binary lives at tests/architecture/.../bin/Debug/net10.0/.
    /// Walk up until we find a directory containing src/ + tests/.
    /// Calls Assert.Fail if not found — a null return would silently skip all literal-scan tests,
    /// turning a broken CI path layout into a false-green. Tests MUST fail hard on path misconfigurations.
    /// </summary>
    private static string FindSrcRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 10; i++)
        {
            if (dir is null) break;
            var srcPath = Path.Combine(dir.FullName, "src");
            var testsPath = Path.Combine(dir.FullName, "tests");
            if (Directory.Exists(srcPath) && Directory.Exists(testsPath))
                return srcPath;
            dir = dir.Parent;
        }

        // Hard fail — returning null would let every scan test silently pass with zero scanned files.
        Assert.Fail(
            $"FindSrcRoot returned null — test environment broken; " +
            $"expected to find 'src/' directory walking up from test binary at: {AppContext.BaseDirectory}. " +
            $"Ensure the test binary is run from within the repository checkout.");
        return null!; // unreachable — Assert.Fail throws; satisfies compiler
    }
}
