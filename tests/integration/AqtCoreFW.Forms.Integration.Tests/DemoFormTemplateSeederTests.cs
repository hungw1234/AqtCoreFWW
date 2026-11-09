using System.Text.Json;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Infrastructure.Persistence;
using AqtCoreFW.Forms.Infrastructure.Seeding;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Seeding;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AqtCoreFW.Forms.Integration.Tests;

/// <summary>
/// Integration coverage for <see cref="DemoFormTemplateSeeder"/> (seed-correctness + publishability).
/// The seeder publishes one intake template per tenant with a diverse field set and a snapshot. These
/// tests drive the static per-tenant path through a filter-free FormsDbContext (mirrors startup) and
/// assert the published template, its static dropdown options, idempotency, and the version snapshot.
/// Cross-tenant writes succeed because the TestContainers connection is db_owner (RLS predicate bypass).
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class DemoFormTemplateSeederTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public DemoFormTemplateSeederTests(WebAppFixture app) => _app = app;

    // Expected distinct field types the demo template must exercise (≥5 incl. the EnumRef dropdown).
    private static readonly FormFieldType[] ExpectedTypes =
    [
        FormFieldType.Text, FormFieldType.Textarea, FormFieldType.Number,
        FormFieldType.Date, FormFieldType.EnumRef, FormFieldType.Boolean, FormFieldType.File,
    ];

    // ── T1: one published template per tenant with the diverse field set + Vietnamese round-trip ──

    [Fact]
    public async Task T1_SeedsOnePublishedTemplate_PerTenant_WithDiverseFieldTypes()
    {
        await SeedTenantAsync(TenantSeedProfiles.BoTaiChinh);
        await SeedTenantAsync(TenantSeedProfiles.BoNoiVu);

        foreach (var profile in TenantSeedProfiles.All)
        {
            await using var ctx = NewFilterFreeCtx();
            var template = await ctx.FormTemplates
                .IgnoreQueryFilters().AsNoTracking()
                .Include(t => t.Fields)
                .SingleAsync(t => t.TenantId == profile.TenantId
                                  && t.Code == DemoFormTemplateSeeder.DemoTemplateCode);

            template.Status.Should().Be(FormTemplateStatus.Active,
                $"{profile.Code} Json-mode template must publish to Active");
            template.StorageMode.Should().Be(StorageMode.Json);
            template.NameVi.Should().Contain("Phiếu tiếp nhận hồ sơ",
                "Vietnamese diacritics must round-trip (NVARCHAR)");

            var activeFields = template.Fields.Where(f => f.IsActive).ToList();
            activeFields.Should().HaveCount(7);
            activeFields.Select(f => f.Type).Distinct().Should()
                .HaveCountGreaterThanOrEqualTo(5, "the demo form must exercise ≥5 distinct field types");
            activeFields.Select(f => f.Type).Should().Contain(ExpectedTypes,
                "every showcased field type must be present");
            activeFields.Single(f => f.FieldKey == DemoFormTemplateSeeder.PriorityFieldKey)
                .Type.Should().Be(FormFieldType.EnumRef);
        }
    }

    // ── T2: the EnumRef dropdown carries its static options with Vietnamese diacritics ──

    [Fact]
    public async Task T2_EnumRefField_HasStaticOptions_WithDiacritics()
    {
        var profile = TenantSeedProfiles.BoTaiChinh;
        await SeedTenantAsync(profile);

        await using var ctx = NewFilterFreeCtx();
        var priority = await ctx.FormFields
            .IgnoreQueryFilters().AsNoTracking()
            .Include(f => f.Options)
            .SingleAsync(f => f.FieldKey == DemoFormTemplateSeeder.PriorityFieldKey
                              && ctx.FormTemplates.Any(t => t.Id == f.FormTemplateId
                                                            && t.TenantId == profile.TenantId
                                                            && t.Code == DemoFormTemplateSeeder.DemoTemplateCode));

        priority.Options.Should().HaveCount(3, "the priority dropdown has Low/Medium/High");
        priority.Options.OrderBy(o => o.DisplayOrder).Select(o => o.Value)
            .Should().Equal("thap", "trung_binh", "cao");
        priority.Options.Select(o => o.LabelVi)
            .Should().Contain(["Thấp", "Trung bình", "Cao"], "option labels keep Vietnamese diacritics");
    }

    // ── T3: re-running the seeder is a no-op (idempotent per TenantId + Code) ──

    [Fact]
    public async Task T3_SecondRun_IsNoOp_NoDuplicateTemplateOrVersion()
    {
        var profile = TenantSeedProfiles.BoNoiVu;

        await SeedTenantAsync(profile);
        var (templatesBefore, versionsBefore) = await CountAsync(profile);

        // Faithful second run through a fresh context (mirrors a second startup).
        await SeedTenantAsync(profile);
        var (templatesAfter, versionsAfter) = await CountAsync(profile);

        templatesBefore.Should().Be(1);
        templatesAfter.Should().Be(1, "idempotency: a second seed run must not create a duplicate template");
        versionsAfter.Should().Be(versionsBefore, "no duplicate version snapshot on re-run");
    }

    // ── T4: the published template carries a version snapshot capturing every active field ──

    [Fact]
    public async Task T4_PublishedTemplate_HasVersionSnapshot_CapturingActiveFields()
    {
        // Throwaway tenant so the assertion never races the shared-tenant defs under batch ordering.
        var profile = MakeThrowawayProfile(Guid.NewGuid());
        await SeedTenantAsync(profile);

        await using var ctx = NewFilterFreeCtx();
        var template = await ctx.FormTemplates
            .IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(t => t.TenantId == profile.TenantId
                              && t.Code == DemoFormTemplateSeeder.DemoTemplateCode);

        var versions = await ctx.FormTemplateVersions
            .IgnoreQueryFilters().AsNoTracking()
            .Where(v => v.FormTemplateId == template.Id)
            .ToListAsync();

        versions.Should().ContainSingle("publish writes exactly one snapshot");
        var snapshot = versions[0];
        snapshot.VersionNumber.Should().Be(1);

        using var doc = JsonDocument.Parse(snapshot.SchemaJson);
        var fields = doc.RootElement.GetProperty("fields");
        fields.GetArrayLength().Should().Be(7, "the snapshot captures all 7 active fields");

        var keys = fields.EnumerateArray().Select(f => f.GetProperty("key").GetString()).ToList();
        keys.Should().Contain(DemoFormTemplateSeeder.PriorityFieldKey,
            "the dropdown field is part of the snapshot schema");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Drives the seeder's static per-tenant path through a fresh filter-free context (mirrors startup).</summary>
    private async Task SeedTenantAsync(TenantSeedProfile profile)
    {
        using var scope = _app.Services.CreateScope();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();
        await using var ctx = NewFilterFreeCtx();
        await DemoFormTemplateSeeder.SeedTenantTemplateAsync(
            ctx, profile, gen, NullLogger.Instance, CancellationToken.None);
    }

    private async Task<(int Templates, int Versions)> CountAsync(TenantSeedProfile profile)
    {
        await using var ctx = NewFilterFreeCtx();
        var templateIds = await ctx.FormTemplates
            .IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.TenantId == profile.TenantId && t.Code == DemoFormTemplateSeeder.DemoTemplateCode)
            .Select(t => t.Id)
            .ToListAsync();

        var versions = await ctx.FormTemplateVersions
            .IgnoreQueryFilters().AsNoTracking()
            .Where(v => templateIds.Contains(v.FormTemplateId))
            .CountAsync();

        return (templateIds.Count, versions);
    }

    /// <summary>Filter-free FormsDbContext (system seed context) — reads/writes span tenants like the seeder.</summary>
    private FormsDbContext NewFilterFreeCtx() =>
        new(_app.Services.GetRequiredService<DbContextOptions<FormsDbContext>>(),
            SystemSeederTenantContext.Instance);

    /// <summary>
    /// Builds a one-off profile for isolation tests. The seeder reads only TenantId / Name / Code;
    /// the remaining required fields carry inert placeholders.
    /// </summary>
    private static TenantSeedProfile MakeThrowawayProfile(Guid tenantId) => new()
    {
        TenantId = tenantId,
        Code = $"throwaway-{tenantId:N}",
        Name = "Throwaway tenant",
        RolePrefix = "TW_",
        UserCodePrefix = "TW-",
        LeadEmailPrefix = "tw.lead.",
        DevEmailPrefix = "tw.dev.",
        FillEmailPrefix = "tw",
        EmailDomain = "@throwaway.test",
        UserCount = 0,
        MaxLevel = 1,
        SeedVersion = "test",
    };
}
