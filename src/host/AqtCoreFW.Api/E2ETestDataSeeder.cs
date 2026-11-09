using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Organization.Domain.Entities;
using AqtCoreFW.Organization.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Hosting;
using AqtCoreFW.SystemParams.Domain.Entities;
using AqtCoreFW.SystemParams.Infrastructure.Persistence;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Api;

/// <summary>
/// Seeds all data required by E2E Playwright tests — only runs in Development environment.
/// Idempotent: checks existence by fixed GUID before inserting. E2E tests reference these GUIDs.
/// Runs after MasterDataSeeder and SystemParamSeeder (both registered before this via AddHostedService order).
/// </summary>
public sealed partial class E2ETestDataSeeder(
    IServiceScopeFactory scopeFactory,
    IWebHostEnvironment env,
    IGuidGenerator guidGen,
    ILogger<E2ETestDataSeeder> logger) : IHostedService, IIntegrationTestSeeder
{
    // ── Fixed GUIDs — E2E tests use these directly ──────────────────────────────────────────────

    // Tenant (dev default — all seeded entities share this)
    public static readonly Guid DevTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    // Actor for audit fields
    private static readonly Guid SystemActorId = Guid.Parse("00000000-0000-0000-0000-000000000000");

    // Org units
    public static readonly Guid OrgBnvId      = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid OrgVtccbId    = Guid.Parse("10000000-0000-0000-0000-000000000002");
    public static readonly Guid OrgPnsId      = Guid.Parse("10000000-0000-0000-0000-000000000003");
    public static readonly Guid OrgVhcId      = Guid.Parse("10000000-0000-0000-0000-000000000004");

    // Workflow — T2 entity; stable PublicId (Guid AK); seeder pins this via PinnedGuidGenerator.
    public static readonly Guid WorkflowDefId = Guid.Parse("20000000-0000-0000-0000-000000000001");

    // Form template — T2 entity (Phase-07 retier); stable PublicId (Guid AK); seeder pins this via PinnedGuidGenerator.
    public static readonly Guid FormTemplateId = Guid.Parse("30000000-0000-0000-0000-000000000001");

    // Announcement — T1 entity (long Id only). AnnouncementId no longer equals DB Id.
    // E2E tests should query by title or check for existence differently.
    [Obsolete("SystemAnnouncement is T1 (no PublicId). Use title for lookup in E2E tests.")]
    public static readonly Guid AnnouncementId = Guid.Parse("50000000-0000-0000-0000-000000000001");

    // lockout-victim@dev.local — dedicated user for TC-AUTH-055 lockout test (QD742 isolation).
    // Pinned Guid Id so afterAll can call /api/v1/admin/users/{id}/unlock without API lookup.
    public static readonly Guid LockoutVictimUserId = Guid.Parse("60000000-0000-0000-0000-000000000001");

    // ────────────────────────────────────────────────────────────────────────────────────────────

    public async Task StartAsync(CancellationToken ct)
    {
        // Guard: only execute in Development — never in staging/production
        if (!env.IsDevelopment())
        {
            logger.LogDebug("E2ETestDataSeeder: skipped (not Development environment)");
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        // P4 defense: per-step wrap so one failure (e.g. vault pepper miss while
        // creating dev users, transient DB error during workflow seed) does not
        // abort the remaining E2E fixture seeds — dev stack still boots usable.
        int failed = 0;
        await TryStepAsync("Permissions",         () => SeedPermissionsAsync(sp, ct));
        await TryStepAsync("RolePermissions",     () => SeedRolePermissionsAsync(sp, ct));
        await TryStepAsync("Menus",               () => SeedMenusAsync(sp, ct));
        await TryStepAsync("AdditionalUsers",     () => SeedAdditionalUsersAsync(sp, ct));
        await TryStepAsync("OrgUnits",            () => SeedOrgUnitsAsync(sp, guidGen, ct));
        await TryStepAsync("StaffAssignments",    () => SeedStaffAssignmentsAsync(sp, ct));
        await TryStepAsync("UserGroups",          () => SeedUserGroupsAsync(sp, ct));
        await TryStepAsync("Workflow",            () => SeedWorkflowAsync(sp, guidGen, ct));
        await TryStepAsync("ParallelWorkflow",    () => SeedParallelWorkflowAsync(sp, logger, ct));
        await TryStepAsync("FormTemplate",        () => SeedFormTemplateAsync(sp, ct));
        await TryStepAsync("Announcement",        () => SeedAnnouncementAsync(sp, ct));

        if (failed > 0)
            logger.LogError("E2ETestDataSeeder: complete — {Failed} step(s) skipped due to errors", failed);
        else
            logger.LogInformation("E2ETestDataSeeder: complete");

        async Task TryStepAsync(string stepName, Func<Task> step)
        {
            try
            {
                await step();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                failed++;
                logger.LogError(ex,
                    "E2ETestDataSeeder: step {Step} failed — continuing with remaining fixtures",
                    stepName);
            }
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    // ── 1. Additional dev users ──────────────────────────────────────────────────────────────────

    private static async Task SeedAdditionalUsersAsync(IServiceProvider sp, CancellationToken ct)
    {
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = sp.GetRequiredService<RoleManager<ApplicationRole>>();
        var config = sp.GetRequiredService<IConfiguration>();

        // Ensure roles exist with proper metadata (Code, RoleType, IsActive)
        var systemRoles = new (string Name, string Code)[]
        {
            ("SystemAdmin", "SYSTEM_ADMIN"), ("Admin", "ADMIN"),
            ("GovOfficer", "GOV_OFFICER"), ("Citizen", "CITIZEN"), ("Viewer", "VIEWER"),
        };
        foreach (var (name, code) in systemRoles)
        {
            var existing = await roleManager.FindByNameAsync(name);
            if (existing is null)
            {
                await roleManager.CreateAsync(new ApplicationRole
                {
                    Name = name, Code = code,
                    RoleType = RoleType.System, IsActive = true,
                });
            }
            else if (string.IsNullOrEmpty(existing.Code))
            {
                // Fix metadata for roles created without Code/RoleType
                existing.Code = code;
                existing.RoleType = RoleType.System;
                existing.IsActive = true;
                await roleManager.UpdateAsync(existing);
            }
        }

        // Passwords from config — no hardcoded secrets in source
        var officerPassword = config["SeedPasswords:OfficerPassword"] ?? "DevOfficer@12345";
        var viewerPassword = config["SeedPasswords:ViewerPassword"] ?? "DevViewer@12345";

        // officer@dev.local — GovOfficer role only (for role-based access tests)
        await EnsureUserAsync(
            userManager,
            email: "officer@dev.local",
            password: officerPassword,
            fullName: "Dev Officer",
            roles: ["GovOfficer"],
            ct);

        // viewer@dev.local — no privileged role (for 403 / read-only tests)
        await EnsureUserAsync(
            userManager,
            email: "viewer@dev.local",
            password: viewerPassword,
            fullName: "Dev Viewer",
            roles: [],
            ct);

        // lockout-victim@dev.local — dedicated user for TC-AUTH-055 lockout test.
        // Minimal Viewer role (blast radius small). Pinned Guid Id for stable unlock call in afterAll.
        // Password is hardcoded intentionally: test-only account, dev env only.
        await EnsureUserAsync(
            userManager,
            email: "lockout-victim@dev.local",
            password: "LockoutVictim@12345",
            fullName: "Lockout Victim",
            roles: ["Viewer"],
            ct,
            pinnedId: LockoutVictimUserId);
    }

    private static async Task EnsureUserAsync(
        UserManager<ApplicationUser> userManager,
        string email,
        string password,
        string fullName,
        string[] roles,
        CancellationToken ct,
        Guid? pinnedId = null)
    {
        _ = ct; // UserManager doesn't accept CancellationToken on these methods

        if (await userManager.FindByEmailAsync(email) is not null)
            return;

        // Fallback by pinned Id — covers the case where email-hash backfill is partial
        // (Phase-04 rollout against pre-existing dev rows) and FindByEmailAsync misses
        // an existing row whose hash column is still NULL/stale.
        if (pinnedId.HasValue
            && await userManager.FindByIdAsync(pinnedId.Value.ToString()) is not null)
            return;

        var user = new ApplicationUser
        {
            // Pin the Guid Id when provided (e.g. lockout-victim) so specs can reference it
            // without an API lookup. UserManager.CreateAsync honours a pre-assigned Id.
            Id = pinnedId ?? Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = fullName,
        };

        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            Console.WriteLine($"[E2ETestDataSeeder] CreateUser '{email}' failed: {errors}");
            return;
        }

        foreach (var role in roles)
            await userManager.AddToRoleAsync(user, role);

        Console.WriteLine($"[E2ETestDataSeeder] Created user '{email}' with roles: [{string.Join(", ", roles)}]");
    }

    // ── 2. Organization units ────────────────────────────────────────────────────────────────────

    private static async Task SeedOrgUnitsAsync(IServiceProvider sp, IGuidGenerator gen, CancellationToken ct)
    {
        var db = sp.GetRequiredService<OrgDbContext>();

        // T2 entity: idempotency by PublicId (stable Guid AK) — not Id (long PK).
        if (await db.OrgUnits.AnyAsync(o => o.PublicId == OrgBnvId, ct))
            return;

        // Phased save: parent must be saved first to obtain its long Id for child FK.
        // Root: Bộ Nội Vụ (Ministry level)
        var bnv = CreateOrgUnit(gen, OrgBnvId, "Bộ Nội Vụ", "Ministry of Home Affairs", "BNV", DevTenantId, parentId: null, level: 1);
        await db.OrgUnits.AddAsync(bnv, ct);
        await db.SaveChangesAsync(ct); // bnv.Id now assigned by DB

        // Department children of BNV — parentId = bnv.Id (long)
        var vtccb = CreateOrgUnit(gen, OrgVtccbId, "Vụ Tổ chức Cán bộ", "Personnel Organization Dept", "VTCCB", DevTenantId, parentId: bnv.Id, level: 2);
        var vhc   = CreateOrgUnit(gen, OrgVhcId,   "Vụ Hành chính",      "Administration Dept",        "VHC",   DevTenantId, parentId: bnv.Id, level: 2);
        await db.OrgUnits.AddRangeAsync([vtccb, vhc], ct);
        await db.SaveChangesAsync(ct); // vtccb.Id, vhc.Id now assigned

        // Division child of VTCCB — parentId = vtccb.Id (long)
        var pns = CreateOrgUnit(gen, OrgPnsId, "Phòng Nhân sự", "HR Division", "PNS", DevTenantId, parentId: vtccb.Id, level: 3);
        await db.OrgUnits.AddAsync(pns, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Creates OrgUnit with pinned PublicId for E2E test stability.</summary>
    private static OrgUnit CreateOrgUnit(
        IGuidGenerator gen, Guid pinnedPublicId,
        string name, string nameEn, string code,
        Guid tenantId, long? parentId, int level)
    {
        // Use a one-shot pinned generator so OrgUnit.Create assigns the desired PublicId.
        var pinnedGen = new PinnedGuidGenerator(pinnedPublicId);
        return OrgUnit.Create(pinnedGen, name, nameEn, code, tenantId, parentId, level);
    }

    // ── 2b. Staff assignments (user → org unit with role) ──────────────────────────────────────

    private static async Task SeedStaffAssignmentsAsync(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<OrgDbContext>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();

        // Idempotency: skip if any assignment exists for this tenant
        if (await db.Assignments.AnyAsync(a => a.TenantId == DevTenantId, ct))
            return;

        // Resolve OrgUnit long Ids by PublicId (T2 entity — FK is long, not Guid).
        var orgIds = await db.OrgUnits
            .Where(o => new[] { OrgBnvId, OrgVtccbId, OrgVhcId, OrgPnsId }.Contains(o.PublicId))
            .Select(o => new { o.PublicId, o.Id })
            .ToListAsync(ct);

        var orgIdMap = orgIds.ToDictionary(o => o.PublicId, o => o.Id);

        var admin   = await userManager.FindByEmailAsync("admin@dev.local");
        var officer = await userManager.FindByEmailAsync("officer@dev.local");
        var viewer  = await userManager.FindByEmailAsync("viewer@dev.local");

        var assignments = new List<UserOrgUnitAssignment>();

        // Admin → Bộ Nội Vụ (Lãnh đạo)
        if (admin is not null && orgIdMap.TryGetValue(OrgBnvId, out var bnvId))
            assignments.Add(UserOrgUnitAssignment.Create(admin.Id, bnvId, "Lãnh đạo", DevTenantId));

        // Officer → Vụ TCCB (Chuyên viên) + Phòng NS (Phó trưởng phòng)
        if (officer is not null)
        {
            if (orgIdMap.TryGetValue(OrgVtccbId, out var vtccbId))
                assignments.Add(UserOrgUnitAssignment.Create(officer.Id, vtccbId, "Chuyên viên", DevTenantId));
            if (orgIdMap.TryGetValue(OrgPnsId, out var pnsId))
                assignments.Add(UserOrgUnitAssignment.Create(officer.Id, pnsId, "Phó trưởng phòng", DevTenantId));
        }

        // Viewer → Vụ HC (Nhân viên)
        if (viewer is not null && orgIdMap.TryGetValue(OrgVhcId, out var vhcId))
            assignments.Add(UserOrgUnitAssignment.Create(viewer.Id, vhcId, "Nhân viên", DevTenantId));

        if (assignments.Count > 0)
        {
            await db.Assignments.AddRangeAsync(assignments, ct);
            await db.SaveChangesAsync(ct);
        }
    }

    // ── 3. Workflow definition ───────────────────────────────────────────────────────────────────

    private static async Task SeedWorkflowAsync(IServiceProvider sp, IGuidGenerator gen, CancellationToken ct)
    {
        var db = sp.GetRequiredService<WorkflowDbContext>();

        // T2 entity: idempotency by PublicId (stable Guid AK) — not Id (long PK).
        if (await db.WorkflowDefinitions.AnyAsync(w => w.PublicId == WorkflowDefId, ct))
            return;

        // Pin the PublicId via a one-shot generator so E2E tests can reference WorkflowDefId.
        var def = WorkflowDefinition.Create(
            gen: new PinnedGuidGenerator(WorkflowDefId),
            name: "GOV Case Processing",
            description: "Standard GOV case processing workflow for E2E tests",
            tenantId: DevTenantId,
            createdBy: SystemActorId);

        // Phase 1: save def + states so EF assigns long Ids to states before transitions reference them.
        // States — must have exactly one IsInitial before Activate()
        var stateDraft       = MakeState(def.Id, "Draft",        "Nháp",              "Draft",        isInitial: true,  isFinal: false, "#808080", 1);
        var stateSubmitted   = MakeState(def.Id, "Submitted",    "Đã nộp",            "Submitted",    isInitial: false, isFinal: false, "#3B82F6", 2);
        var stateUnderReview = MakeState(def.Id, "UnderReview",  "Đang xem xét",      "Under Review", isInitial: false, isFinal: false, "#F59E0B", 3);
        var stateApproved    = MakeState(def.Id, "Approved",     "Đã phê duyệt",      "Approved",     isInitial: false, isFinal: false, "#10B981", 4);
        var stateRejected    = MakeState(def.Id, "Rejected",     "Đã từ chối",        "Rejected",     isInitial: false, isFinal: false, "#EF4444", 5);
        var stateClosed      = MakeState(def.Id, "Closed",       "Đã đóng",           "Closed",       isInitial: false, isFinal: true,  "#6B7280", 6);

        def.AddState(stateDraft);
        def.AddState(stateSubmitted);
        def.AddState(stateUnderReview);
        def.AddState(stateApproved);
        def.AddState(stateRejected);
        def.AddState(stateClosed);

        await db.WorkflowDefinitions.AddAsync(def, ct);
        await db.SaveChangesAsync(ct); // state Ids now assigned by DB IDENTITY

        // Phase 2: add transitions now that state.Id (long) values are available.
        // WorkflowTransition.Create takes (long defId, long fromStateId, long toStateId, ...)
        // Handler authz is driven by WorkflowTransitionAssignment rules (assignment-based).
        def.AddTransition(MakeTransition(def.Id, stateDraft.Id,       stateSubmitted.Id,   "Submit",  "Nộp hồ sơ",    "Submit",  null, 1));
        def.AddTransition(MakeTransition(def.Id, stateSubmitted.Id,   stateUnderReview.Id, "Review",  "Xem xét",      "Review",  null, 2));
        def.AddTransition(MakeTransition(def.Id, stateUnderReview.Id, stateApproved.Id,    "Approve", "Phê duyệt",    "Approve", null, 3));
        def.AddTransition(MakeTransition(def.Id, stateUnderReview.Id, stateRejected.Id,    "Reject",  "Từ chối",      "Reject",  null, 4));
        def.AddTransition(MakeTransition(def.Id, stateApproved.Id,    stateClosed.Id,      "Close",   "Đóng hồ sơ",  "Close",   null, 5));
        def.AddTransition(MakeTransition(def.Id, stateRejected.Id,    stateClosed.Id,      "Close",   "Đóng hồ sơ",  "Close",   null, 6));

        def.Activate(SystemActorId);
        await db.SaveChangesAsync(ct);
    }

    private static WorkflowState MakeState(
        long defId, string name, string nameVi, string nameEn,
        bool isInitial, bool isFinal, string color, int order)
    {
        return WorkflowState.Create(defId, name, nameVi, nameEn, isInitial, isFinal, color, order);
    }

    private static WorkflowTransition MakeTransition(
        long defId, long fromId, long toId,
        string action, string labelVi, string labelEn,
        string? conditions, int order)
    {
        return WorkflowTransition.Create(defId, fromId, toId, action, labelVi, labelEn, conditions, order);
    }

    // ── 4. Form template ─────────────────────────────────────────────────────────────────────────

    private static async Task SeedFormTemplateAsync(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AqtCoreFW.Forms.Infrastructure.Persistence.FormsDbContext>();

        // T2 entity: Code is the stable natural key for idempotency.
        if (await db.FormTemplates.AnyAsync(f => f.Code == "citizen-request" && f.TenantId == DevTenantId, ct))
            return;

        var template = FormTemplate.Create(
            gen: new PinnedGuidGenerator(FormTemplateId),
            tenantId: DevTenantId,
            name: "Citizen Request Form",
            nameVi: "Đơn yêu cầu công dân",
            code: "citizen-request",
            storageMode: StorageMode.Json,
            createdBy: SystemActorId);

        // Add fields while still in Draft (Publish requires at least 1 field)
        template.AddField("full_name",       FormFieldType.Text,     "Họ và tên",        "Full Name",       1, required: true,  null, SystemActorId);
        template.AddField("phone",           FormFieldType.Text,     "Số điện thoại",    "Phone",           2, required: false, null, SystemActorId);
        template.AddField("address",         FormFieldType.Text,     "Địa chỉ",          "Address",         3, required: false, null, SystemActorId);
        template.AddField("request_details", FormFieldType.Textarea, "Chi tiết yêu cầu", "Request Details", 4, required: true,  null, SystemActorId);

        // Two-phase (EF IDENTITY-before-save): persist the Draft FIRST so template.Id is assigned,
        // THEN PublishAndSnapshot — the snapshot captures template.Id, which is 0 until the row is
        // saved; building it pre-save violates FK_FormTemplateVersions_FormTemplates. The real
        // command handler is immune because it loads an already-persisted template. PublishAndSnapshot
        // pairs publish+snapshot so the status-Active-without-snapshot drift cannot recur
        // (root-caused 2026-05-18).
        await db.FormTemplates.AddAsync(template, ct);
        await db.SaveChangesAsync(ct);

        var snapshot = FormTemplateVersionFactory.PublishAndSnapshot(template, SystemActorId);
        await db.FormTemplateVersions.AddAsync(snapshot, ct);
        await db.SaveChangesAsync(ct);
    }

    // ── 5. System announcement ───────────────────────────────────────────────────────────────────

    private static async Task SeedAnnouncementAsync(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<SystemParamsDbContext>();

        // T1 entity: no PublicId, title is the stable natural key for idempotency.
        const string e2eAnnouncementTitle = "Thông báo bảo trì hệ thống";
        if (await db.SystemAnnouncements.AnyAsync(a => a.Title == e2eAnnouncementTitle, ct))
            return;

        var now = DateTimeOffset.UtcNow;
        var announcement = SystemAnnouncement.Create(
            title: e2eAnnouncementTitle,
            content: "Hệ thống sẽ bảo trì định kỳ vào cuối tuần. Vui lòng hoàn thành công việc trước 18:00 thứ Sáu.",
            severity: AnnouncementSeverity.Info,
            startAt: now,
            endAt: now.AddDays(30));

        await db.SystemAnnouncements.AddAsync(announcement, ct);
        await db.SaveChangesAsync(ct);
    }

    // ── PinnedGuidGenerator — returns one pinned Guid for E2E seed idempotency ─────────────────────

    /// <summary>
    /// One-shot IGuidGenerator that returns a pre-pinned Guid.
    /// Used in seeders to ensure T2 entities get a stable PublicId across restarts.
    /// Falls back to Guid.NewGuid() for any subsequent calls (should not occur in seeder scope).
    /// </summary>
    private sealed class PinnedGuidGenerator(Guid pinned) : IGuidGenerator
    {
        private Guid _next = pinned;

        public Guid NewIdMonotonic() { var v = _next; _next = Guid.NewGuid(); return v; }
        public Guid NewIdRandom()    { var v = _next; _next = Guid.NewGuid(); return v; }
    }
}
