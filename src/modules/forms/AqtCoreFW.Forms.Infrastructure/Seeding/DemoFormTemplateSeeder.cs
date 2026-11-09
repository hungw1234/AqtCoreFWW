using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Hosting;
using AqtCoreFW.SharedKernel.Application.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Forms.Infrastructure.Seeding;

/// <summary>
/// Tenant-agnostic demo form-template seeder: seeds one published intake template per
/// <see cref="TenantSeedProfiles"/> entry (BTC + BNV). The template exercises a diverse field set
/// (Text / Textarea / Number / Date / EnumRef-with-static-options / Boolean / File) so the demo
/// workflow can bind it as an intake form. All labels are Vietnamese (NVARCHAR round-trip).
///
/// The dropdown field uses <see cref="FormFieldType.EnumRef"/> with static <see cref="FormFieldOption"/>
/// rows (the "Phase 1 pre-seeded options" model). Static options take precedence over DataSourceJson
/// resolution in GetFormFieldOptionsQueryHandler, so no data-source config is needed.
///
/// Storage mode is <see cref="StorageMode.Json"/>: Publish flips Draft→Active immediately (Table mode
/// would route through background DDL materialization — out of scope for a definitions-only seed).
///
/// Config-gated (<c>Forms:DemoFormTemplateSeed</c>, default OFF) and idempotent per (TenantId, Code) —
/// the unique (TenantId, Code) index makes the existence probe load-bearing. Like the workflow demo
/// seeder there is no tenant-existence guard: a template's TenantId is an unenforced stamp, so a
/// missing tenant yields a harmless never-used template rather than an orphaned-FK row.
///
/// Cross-tenant writes from a single startup scope rely on the dev/seed connection being db_owner
/// (the RLS predicate bypasses for db_owner; <see cref="SystemSeederTenantContext"/> leaves
/// SESSION_CONTEXT unset). Mirrors the Organization/Workflow seeders.
/// </summary>
public sealed class DemoFormTemplateSeeder(
    IServiceScopeFactory scopeFactory,
    IOptions<DemoFormTemplateSeederOptions> options,
    IGuidGenerator gen,
    ILogger<DemoFormTemplateSeeder> logger) : IHostedService, IIntegrationTestSeeder
{
    private readonly DemoFormTemplateSeederOptions _opts = options.Value;

    /// <summary>Stable template code (shared across tenants; uniqueness is per TenantId).</summary>
    public const string DemoTemplateCode = "demo-intake-form";

    /// <summary>FieldKey of the EnumRef (dropdown) field — exposed so tests can assert its options without guessing.</summary>
    public const string PriorityFieldKey = "muc_do_uu_tien";

    // Seeder-authored rows are attributed to the system actor (all-zero Guid, mirrors E2E seeder).
    private static readonly Guid SystemActorId = Guid.Empty;

    public async Task StartAsync(CancellationToken ct)
    {
        if (!_opts.Enabled)
        {
            logger.LogDebug("[DemoFormTemplateSeeder] Disabled — skipping form template demo seed");
            return;
        }

        foreach (var profile in TenantSeedProfiles.All)
        {
            using var scope = scopeFactory.CreateScope();
            var dbOptions = scope.ServiceProvider.GetRequiredService<DbContextOptions<FormsDbContext>>();

            // System seed context bypasses the tenant global filter so the idempotency probe + inserts
            // span both tenants from one startup scope (mirror OrgTreeSeeder / DemoWorkflowSeeder).
            await using var ctx = new FormsDbContext(dbOptions, SystemSeederTenantContext.Instance);

            await SeedTenantTemplateAsync(ctx, profile, gen, logger, ct);
        }

        logger.LogInformation("[DemoFormTemplateSeeder] All tenants complete");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Seeds the demo template for one tenant. Idempotent per (TenantId, Code): a second run is a no-op.
    /// Two-phase save (EF IDENTITY-before-snapshot): (1) template + fields + Select options inserted in
    /// one SaveChanges (field Ids assigned by IDENTITY; option FKs fixed up via the mapped Options nav,
    /// so passing field.Id=0 at AddOption time is harmless), then (2) PublishAndSnapshot captures the
    /// now-assigned template.Id + field Ids into the FormTemplateVersion row. Static + ctx-injected so
    /// tests can drive it directly.
    /// </summary>
    public static async Task SeedTenantTemplateAsync(
        FormsDbContext ctx,
        TenantSeedProfile profile,
        IGuidGenerator gen,
        ILogger logger,
        CancellationToken ct)
    {
        var exists = await ctx.FormTemplates
            .IgnoreQueryFilters()
            .AnyAsync(t => t.TenantId == profile.TenantId && t.Code == DemoTemplateCode, ct);
        if (exists)
        {
            logger.LogInformation(
                "[DemoFormTemplateSeeder] {Code}: demo template already present — skipping", profile.Code);
            return;
        }

        // The template+fields+options and the publish+snapshot span two SaveChanges (the snapshot pins
        // IDENTITY ids the first phase assigned). Wrap both in one transaction so a fault between them
        // cannot leave a committed Draft template with no published version: the idempotency probe keys
        // on the root (TenantId, Code), so a half-wired survivor would be skipped (never repaired) on a
        // plain restart. reset-all nukes first and self-heals; a live-DB crash would not.
        await using var tx = await ctx.Database.BeginTransactionAsync(ct);

        var template = FormTemplate.Create(
            gen,
            tenantId: profile.TenantId,
            name: "Demo intake form",
            nameVi: $"Phiếu tiếp nhận hồ sơ (demo) — {profile.Name}",
            code: DemoTemplateCode,
            storageMode: StorageMode.Json,
            createdBy: SystemActorId);

        // Diverse field set (≥5 types incl. EnumRef dropdown with options). FieldKeys are SQL-safe ASCII;
        // labels are Vietnamese. AddField is valid in Draft (Publish later requires ≥1 active field).
        template.AddField("ho_ten",      FormFieldType.Text,     "Họ và tên",            "Full name",        1, required: true,  null, SystemActorId);
        template.AddField("noi_dung",    FormFieldType.Textarea, "Nội dung đề nghị",     "Request details",  2, required: true,  null, SystemActorId);
        template.AddField("so_tien",     FormFieldType.Number,   "Số tiền đề nghị (VND)", "Requested amount", 3, required: false, null, SystemActorId);
        template.AddField("ngay_de_nghi", FormFieldType.Date,    "Ngày đề nghị",         "Request date",     4, required: true,  null, SystemActorId);
        var priority = template.AddField(PriorityFieldKey, FormFieldType.EnumRef, "Mức độ ưu tiên", "Priority", 5, required: true, null, SystemActorId);
        template.AddField("khan_cap",    FormFieldType.Boolean,  "Khẩn cấp",             "Urgent",           6, required: false, null, SystemActorId);
        template.AddField("tai_lieu",    FormFieldType.File,     "Tài liệu đính kèm",    "Attachment",       7, required: false, null, SystemActorId);

        // Static dropdown options (Vietnamese, diacritics). field.Id is 0 pre-save; the mapped Options
        // nav drives the FK on cascade insert (same path as AddFormFieldCommandHandler).
        priority.AddOption(FormFieldOption.Create(priority.Id, "thap",        "Thấp",        "Low",    1));
        priority.AddOption(FormFieldOption.Create(priority.Id, "trung_binh",  "Trung bình",  "Medium", 2));
        priority.AddOption(FormFieldOption.Create(priority.Id, "cao",         "Cao",         "High",   3));

        // Phase 1: template + fields + options in one UnitOfWork (template.Id assigned by IDENTITY).
        await ctx.FormTemplates.AddAsync(template, ct);
        await ctx.SaveChangesAsync(ct);

        // Phase 2: publish (Json → Active) + immutable snapshot in one UnitOfWork. PublishAndSnapshot
        // pairs the two so an Active-without-snapshot drift can't recur (root-caused 2026-05-18).
        var snapshot = FormTemplateVersionFactory.PublishAndSnapshot(template, SystemActorId);
        await ctx.FormTemplateVersions.AddAsync(snapshot, ct);
        await ctx.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        logger.LogInformation(
            "[DemoFormTemplateSeeder] {Code}: seeded published demo template with {Fields} fields",
            profile.Code, template.Fields.Count);
    }
}
