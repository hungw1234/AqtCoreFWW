using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace AqtCoreFW.Api;

/// <summary>
/// Partial: seeds production AppMenus + MenuRolePermissions matching FE sidebar structure (Option C flat, 2026-04-24).
/// Per-row idempotent [RT-04 v2]: inserts menus/perms not already present in DB, and reconciles
/// mutable display fields (Title/Description/Icon/Route/SortOrder) on existing rows so a corrected
/// label in <see cref="BuildMenuList"/> self-heals on the next boot without a purge/reseed.
/// Safe across structure evolutions — no manual purge needed when adding new menus.
/// Flushes Redis menu cache after any insert, label update, or purge [RT-06].
///
/// Phase-4b B6c update: AppMenu is T2 (long Id + Guid PublicId).
///   - Idempotency now uses PublicId (Guid AK) instead of Id (long PK).
///   - M() helper uses AppMenu.Create with PinnedGuidGenerator to fix PublicId.
///   - MenuRolePermission.MenuId is long — resolved post-save via PublicId→Id map.
///
/// STRUCTURE (Option C — flat top-level, 2026-04-24):
///   ▸ Nghiep vu (group)              → Dashboard, Cases, Inbox, Forms, Files
///   ▸ AI & Tro ly (group)             → AI Search, Copilot, Chat
///   ▸ Tich hop & Bao cao (group)      → Integration ▸, Audit, Reporting ▸
///   ▸ Dinh danh & Quyen (submenu)     → 11 items
///   ▸ Noi dung & Quy trinh (submenu)  → 6 items
///   ▸ He thong (submenu)              → 6 items
///   ▸ API & Webhooks (submenu)        → 2 items
///   ▸ AI & Automation (submenu)       → 1 item
///   ▸ Bao mat & Tuan thu (group)      → IP whitelist, Audit policy, Backup policy, Signature config, Health ▸ (6 leaves)
///   (▸ Doi tac tich hop removed 2026-05-14 — alias to business chain "Tich hop > Doi tac")
///
/// REMOVING/RENAMING menus [MTS-3, 2026-05-12]: structure entries deleted from <see cref="BuildMenuList"/>
/// are now auto-purged on seed — <see cref="PurgeOrphanMenusAsync"/> deletes any seeder-owned row
/// (PublicId in the <see cref="SeededMenuPublicIdPrefix"/> family, system-scoped TenantId == null)
/// that is no longer in the desired set. Admin-created menus (CreateMenuCommandHandler → Guid v7
/// PublicIds) never match the prefix → are never touched. No manual DELETE needed any more.
/// </summary>
public sealed partial class E2ETestDataSeeder
{
    /// <summary>
    /// PublicId family this seeder owns. Every menu row produced by <see cref="M"/> is pinned in
    /// <c>60000000-0000-0000-0000-XXXXXXXXXXXX</c>; admin-created menus use Guid v7 and never collide,
    /// so the orphan purge can safely scope deletions to this prefix.
    /// </summary>
    internal const string SeededMenuPublicIdPrefix = "60000000-0000-0000-0000-";

    // ── Fixed menu GUIDs (stable PublicId AK for E2E test stability) ─────────────────────────────
    // Range: 60000000-0000-0000-0000-XXXXXXXXXXXX
    // These Guids are pinned as AppMenu.PublicId (not Id which is long DB-assigned).

    // Top-level visual group headers (Option C — Route=null, Icon=null → FE renders type:'group')
    static readonly Guid MenuGrpBusinessId            = Guid.Parse("60000000-0000-0000-0000-000000000090");
    static readonly Guid MenuGrpAiAssistantId         = Guid.Parse("60000000-0000-0000-0000-000000000091");
    static readonly Guid MenuGrpIntegrationReportsId  = Guid.Parse("60000000-0000-0000-0000-000000000092");

    // Business group children
    static readonly Guid MenuDashboardId   = Guid.Parse("60000000-0000-0000-0000-000000000001");
    static readonly Guid MenuInboxId       = Guid.Parse("60000000-0000-0000-0000-000000000003");
    static readonly Guid MenuFormsId       = Guid.Parse("60000000-0000-0000-0000-000000000004");
    static readonly Guid MenuFilesId       = Guid.Parse("60000000-0000-0000-0000-000000000005");

    // AI & Assistant group children
    static readonly Guid MenuAiSearchId    = Guid.Parse("60000000-0000-0000-0000-000000000007");
    static readonly Guid MenuCopilotId     = Guid.Parse("60000000-0000-0000-0000-000000000008");
    static readonly Guid MenuChatId        = Guid.Parse("60000000-0000-0000-0000-000000000009");

    // Integration & Reports group children
    static readonly Guid MenuIntegrationId = Guid.Parse("60000000-0000-0000-0000-00000000000B");
    static readonly Guid MenuIntPartnersId = Guid.Parse("60000000-0000-0000-0000-000000000020");
    static readonly Guid MenuIntContractsId= Guid.Parse("60000000-0000-0000-0000-000000000021");
    static readonly Guid MenuIntMsgLogsId  = Guid.Parse("60000000-0000-0000-0000-000000000022");
    static readonly Guid MenuAuditId       = Guid.Parse("60000000-0000-0000-0000-00000000000E");
    static readonly Guid MenuReportingId   = Guid.Parse("60000000-0000-0000-0000-000000000011");
    static readonly Guid MenuReportDefinitionsId = Guid.Parse("60000000-0000-0000-0000-000000000023");
    static readonly Guid MenuReportExecutionsId  = Guid.Parse("60000000-0000-0000-0000-000000000024");

    // Admin top-level submenus (Option C — moved from child-of-MenuAdminId to top-level)
    static readonly Guid MenuGrpIdentityId            = Guid.Parse("60000000-0000-0000-0000-000000000040");
    static readonly Guid MenuGrpContentId             = Guid.Parse("60000000-0000-0000-0000-000000000041");
    static readonly Guid MenuGrpSystemId              = Guid.Parse("60000000-0000-0000-0000-000000000042");
    static readonly Guid MenuGrpApiWebhooksId         = Guid.Parse("60000000-0000-0000-0000-000000000044");
    static readonly Guid MenuGrpAiAutomationId        = Guid.Parse("60000000-0000-0000-0000-000000000045");
    static readonly Guid MenuGrpSecurityId            = Guid.Parse("60000000-0000-0000-0000-000000000046");
    // MenuGrpPartnerIntegrationId removed 2026-05-14: admin alias chain dropped — Partners reachable via
    //   business chain "Tich hop & Bao cao > Tich hop > Doi tac" (MenuIntPartnersId). PublicId kept in
    //   seeded family so PurgeOrphanMenusAsync auto-deletes any stale DB row carrying it.
    // Removed 2026-05-11 ("/admin" landing route deleted). Kept as a documented historical PublicId —
    // it is in the seeded family, so PurgeOrphanMenusAsync auto-deletes any stale DB row carrying it.
    static readonly Guid MenuAdminOverviewId          = Guid.Parse("60000000-0000-0000-0000-000000000030");

    // Admin > Identity children
    static readonly Guid MenuUsersId       = Guid.Parse("60000000-0000-0000-0000-000000000050");
    static readonly Guid MenuGroupsId      = Guid.Parse("60000000-0000-0000-0000-000000000051");
    static readonly Guid MenuExtIdsId      = Guid.Parse("60000000-0000-0000-0000-000000000052");
    static readonly Guid MenuJitId         = Guid.Parse("60000000-0000-0000-0000-000000000053");
    static readonly Guid MenuAbacId        = Guid.Parse("60000000-0000-0000-0000-000000000054");
    static readonly Guid MenuSodId         = Guid.Parse("60000000-0000-0000-0000-000000000055");
    static readonly Guid MenuPolicyRulesId = Guid.Parse("60000000-0000-0000-0000-000000000056");
    static readonly Guid MenuCredPolId     = Guid.Parse("60000000-0000-0000-0000-000000000057");
    static readonly Guid MenuAccessRevId   = Guid.Parse("60000000-0000-0000-0000-000000000058");
    static readonly Guid MenuDelegId       = Guid.Parse("60000000-0000-0000-0000-000000000059");
    static readonly Guid MenuSessionsId    = Guid.Parse("60000000-0000-0000-0000-00000000005A");
    static readonly Guid MenuRolesId       = Guid.Parse("60000000-0000-0000-0000-00000000000D");
    static readonly Guid MenuDataScopesId  = Guid.Parse("60000000-0000-0000-0000-000000000083");

    // Admin > Content children
    static readonly Guid MenuWorkflowId    = Guid.Parse("60000000-0000-0000-0000-000000000060");
    static readonly Guid MenuRulesId       = Guid.Parse("60000000-0000-0000-0000-000000000061");
    static readonly Guid MenuTemplatesId   = Guid.Parse("60000000-0000-0000-0000-000000000062");
    static readonly Guid MenuNotifTplId    = Guid.Parse("60000000-0000-0000-0000-000000000063");
    static readonly Guid MenuMenusId       = Guid.Parse("60000000-0000-0000-0000-000000000064");
    static readonly Guid MenuBusinessProcessesId = Guid.Parse("60000000-0000-0000-0000-000000000065");

    // Admin > System children
    static readonly Guid MenuSysParamsId   = Guid.Parse("60000000-0000-0000-0000-000000000070");
    static readonly Guid MenuMasterDataId  = Guid.Parse("60000000-0000-0000-0000-000000000071");
    static readonly Guid MenuOrgId         = Guid.Parse("60000000-0000-0000-0000-000000000072");
    static readonly Guid MenuJobsId        = Guid.Parse("60000000-0000-0000-0000-000000000073");
    static readonly Guid MenuHealthId      = Guid.Parse("60000000-0000-0000-0000-000000000074");
    static readonly Guid MenuBackupId      = Guid.Parse("60000000-0000-0000-0000-000000000075");
    static readonly Guid MenuRtbfId        = Guid.Parse("60000000-0000-0000-0000-000000000076");
    static readonly Guid MenuTenantsId     = Guid.Parse("60000000-0000-0000-0000-000000000077");

    // Admin > API & Webhooks children
    static readonly Guid MenuApiKeysId     = Guid.Parse("60000000-0000-0000-0000-000000000080");
    static readonly Guid MenuWebhooksId    = Guid.Parse("60000000-0000-0000-0000-000000000081");

    // Admin > AI & Automation children
    static readonly Guid MenuAiAdminId     = Guid.Parse("60000000-0000-0000-0000-000000000082");

    // Admin > Identity > MFA Policies (Phase-05 QĐ 742 — Security & Compliance overlap; FE places under Identity)
    static readonly Guid MenuMfaPoliciesId = Guid.Parse("60000000-0000-0000-0000-000000000084");

    // Admin > Security & Compliance children (Phase-05 QĐ 742 + phase-05b digital signature)
    // Mirror admin-menu-entries.ts getSecurityMenuChildren(): 4 direct leaves + 1 health submenu (6 leaves)
    static readonly Guid MenuIpWhitelistId         = Guid.Parse("60000000-0000-0000-0000-0000000000A0");
    static readonly Guid MenuAuditPolicyId         = Guid.Parse("60000000-0000-0000-0000-0000000000A1");
    static readonly Guid MenuBackupPolicyId        = Guid.Parse("60000000-0000-0000-0000-0000000000A2");
    static readonly Guid MenuSignatureConfigId     = Guid.Parse("60000000-0000-0000-0000-0000000000A3");
    static readonly Guid MenuSignatureEnrollmentsId = Guid.Parse("60000000-0000-0000-0000-0000000000AB");
    static readonly Guid MenuGrpSecurityHealthId   = Guid.Parse("60000000-0000-0000-0000-0000000000A4");
    static readonly Guid MenuSecHlthLoginPolicyId       = Guid.Parse("60000000-0000-0000-0000-0000000000A5");
    static readonly Guid MenuSecHlthPasswordPolicyId    = Guid.Parse("60000000-0000-0000-0000-0000000000A6");
    static readonly Guid MenuSecHlthAuditRetentionId    = Guid.Parse("60000000-0000-0000-0000-0000000000A7");
    static readonly Guid MenuSecHlthAuditIntegrityId    = Guid.Parse("60000000-0000-0000-0000-0000000000A8");
    static readonly Guid MenuSecHlthKeyRotationId       = Guid.Parse("60000000-0000-0000-0000-0000000000A9");
    static readonly Guid MenuSecHlthBackupHistoryId     = Guid.Parse("60000000-0000-0000-0000-0000000000AA");

    // MenuItmPartnersAliasId removed 2026-05-14 (see MenuGrpPartnerIntegrationId note above).

    // ── Seed method ──────────────────────────────────────────────────────────

    private async Task SeedMenusAsync(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<IdentityDbContext>();

        var desiredMenus    = BuildMenuList();
        var desiredPermTuples = BuildMenuPermissions(); // returns (Guid PublicId, string PermCode)

        // RT-04 v2: per-row idempotency via PublicId (stable Guid AK).
        // AppMenu is T2: Id=long (DB-assigned IDENTITY), PublicId=Guid (pinned stable key).
        var desiredPublicIds = desiredMenus.Select(m => m.PublicId).ToList();
        var existingPublicIds = (await db.AppMenus
            .Where(m => desiredPublicIds.Contains(m.PublicId))
            .Select(m => m.PublicId)
            .ToListAsync(ct))
            .ToHashSet();

        var newMenus = desiredMenus.Where(m => !existingPublicIds.Contains(m.PublicId)).ToList();
        if (newMenus.Count > 0)
        {
            await db.AppMenus.AddRangeAsync(newMenus, ct);
            await db.SaveChangesAsync(ct); // AppMenu.Id (long) now assigned by DB IDENTITY
        }

        // Reconcile mutable display fields (Title/Description/Icon/Route/SortOrder) on rows that
        // already exist. The insert-only idempotency above never refreshes a renamed label, so a
        // source edit to BuildMenuList() (e.g. correcting a title) would never reach an
        // already-seeded DB — the stale label would persist until a full purge/reseed. Compare each
        // existing row against its desired counterpart and Update() on any diff, so labels self-heal
        // on every boot without touching tenant/user data. PublicId (stable AK) is never mutated.
        // Scope: display fields only. Structural changes to an existing row — re-parenting
        // (ParentId) or renaming the Code slug — are NOT reconciled here and still require a
        // purge/reseed (Code is part of the unique index; ParentId has its own UpdateParentId path).
        var updatedMenus = 0;
        if (existingPublicIds.Count > 0)
        {
            var desiredByPublicId = desiredMenus.ToDictionary(m => m.PublicId);
            var existingMenus = await db.AppMenus
                .Where(m => existingPublicIds.Contains(m.PublicId))
                .ToListAsync(ct);

            foreach (var current in existingMenus)
            {
                if (!desiredByPublicId.TryGetValue(current.PublicId, out var want)) continue;
                if (current.Title == want.Title
                    && current.Description == want.Description
                    && current.Icon == want.Icon
                    && current.Route == want.Route
                    && current.SortOrder == want.SortOrder)
                    continue; // unchanged — skip

                current.Update(want.Title, want.Description, want.Icon, want.Route, want.SortOrder, Guid.Empty);
                updatedMenus++;
            }

            if (updatedMenus > 0)
                await db.SaveChangesAsync(ct);
        }

        // Build PublicId → long Id map for all desired menus (existing + newly saved).
        // Required because MenuRolePermission.MenuId is long (FK to AppMenu.Id).
        var menuIdMap = (await db.AppMenus
            .Where(m => desiredPublicIds.Contains(m.PublicId))
            .Select(m => new { m.PublicId, m.Id })
            .ToListAsync(ct))
            .ToDictionary(m => m.PublicId, m => m.Id);

        // MenuRolePermission composite PK is (MenuId long, PermissionCode string) — dedupe.
        var desiredLongMenuIds = menuIdMap.Values.ToList();
        var existingPermKeys = (await db.Set<MenuRolePermission>()
            .Where(rp => desiredLongMenuIds.Contains(rp.MenuId))
            .Select(rp => new { rp.MenuId, rp.PermissionCode })
            .ToListAsync(ct))
            .Select(k => (k.MenuId, k.PermissionCode))
            .ToHashSet();

        var newPerms = desiredPermTuples
            .Where(p => menuIdMap.TryGetValue(p.MenuPublicId, out var menuLongId)
                        && !existingPermKeys.Contains((menuLongId, p.PermCode)))
            .Select(p => new MenuRolePermission
            {
                MenuId = menuIdMap[p.MenuPublicId],
                PermissionCode = p.PermCode
            })
            .ToList();

        if (newPerms.Count > 0)
        {
            await db.Set<MenuRolePermission>().AddRangeAsync(newPerms, ct);
            await db.SaveChangesAsync(ct);
        }

        // RT-04 v2 only ADDS missing rows; entries removed from BuildMenuList() leave orphan rows
        // that surface as FE-sidebar 404s ("admin-overview" leaf, bug #14, 2026-05-11). Purge them.
        var purged = await PurgeOrphanMenusAsync(db, desiredPublicIds, ct);

        if (newMenus.Count > 0 || updatedMenus > 0 || newPerms.Count > 0 || purged > 0)
        {
            logger.LogInformation(
                "[Menus] seeded {NewMenus} new menus, updated {UpdatedMenus} label(s), {NewPerms} new permissions, purged {Purged} orphan(s)",
                newMenus.Count, updatedMenus, newPerms.Count, purged);
            // RT-06: flush Redis menu cache whenever structure OR labels actually changed
            // (insert, title reconcile, or purge) — stale menu-tree:* would otherwise mask the update.
            await FlushMenuCacheAsync(sp);
        }
    }

    /// <summary>
    /// Deletes seeder-owned menu rows (PublicId in the <see cref="SeededMenuPublicIdPrefix"/> family,
    /// system-scoped <c>TenantId == null</c>) that are no longer present in the desired structure.
    /// Children are deleted before parents to satisfy the self-referencing <c>ParentId</c> FK
    /// (<see cref="Microsoft.EntityFrameworkCore.DeleteBehavior.Restrict"/>); dependent
    /// <c>MenuRolePermissions</c> rows go via the DB's <c>ON DELETE CASCADE</c>.
    /// <para>
    /// Uses <c>ExecuteDeleteAsync</c> (not <c>RemoveRange + SaveChangesAsync</c>) on purpose:
    /// <c>SoftDeleteInterceptor</c> would otherwise rewrite the DELETE as an
    /// <c>UPDATE … SET IsDeleted=1</c>, and the DB's CASCADE on
    /// <c>FK_MenuRolePermissions_AppMenus_MenuId</c> would NOT fire — leaving permission rows
    /// orphaned forever. <c>ExecuteDeleteAsync</c> bypasses all <c>SavingChanges</c> interceptors,
    /// issues a real SQL <c>DELETE</c>, and the FK CASCADE removes dependents in the same statement.
    /// Hard-delete is the correct semantics here: these are seeder-owned scaffolding rows with no
    /// audit-trail requirement (MTS-3-FU3, 2026-05-13).
    /// </para>
    /// <para>
    /// Upstream query uses <c>IgnoreQueryFilters()</c> so any orphans soft-deleted by pre-fix runs
    /// also get cleaned up (the global <c>!IsDeleted</c> filter would otherwise hide them forever).
    /// </para>
    /// Tolerates a concurrent purge by another host replica (this seeder only runs in Development) —
    /// returned-affected count below expected is logged at Debug, not thrown. Returns the number of
    /// menu rows actually deleted.
    /// </summary>
    private async Task<int> PurgeOrphanMenusAsync(
        IdentityDbContext db, IReadOnlyCollection<Guid> desiredPublicIds, CancellationToken ct)
    {
        var desired = desiredPublicIds as HashSet<Guid> ?? desiredPublicIds.ToHashSet();

        // Materialize first → prefix filtering below runs client-side (no Guid→string SQL translation).
        // IgnoreQueryFilters → also see rows soft-deleted by pre-MTS-3-FU3 runs; they're still in the
        // seeded family and still own dangling MenuRolePermissions → must be hard-deleted.
        var systemMenus = await db.AppMenus
            .IgnoreQueryFilters()
            .Where(m => m.TenantId == null)
            .ToListAsync(ct);

        var orphans = systemMenus
            .Where(m => IsSeededMenuFamily(m.PublicId) && !desired.Contains(m.PublicId))
            .ToList();

        if (orphans.Count == 0) return 0;

        logger.LogWarning("[Menus] {Count} orphan menu(s) not in seed structure — purging: {Codes}",
            orphans.Count, string.Join(", ", orphans.Select(m => m.Code)));

        var deleted = 0;
        var raced = false;
        foreach (var layer in OrderOrphanMenusLeafFirst(orphans, m => m.PublicId, m => m.ParentId))
        {
            var ids = layer.Select(m => m.Id).ToList();
            try
            {
                // ExecuteDeleteAsync bypasses SoftDeleteInterceptor → real SQL DELETE → DB CASCADE
                // on FK_MenuRolePermissions_AppMenus_MenuId fires automatically, removing dependent
                // permission rows in the same statement. IgnoreQueryFilters required so we can also
                // delete rows already marked IsDeleted=true by pre-fix runs.
                var affected = await db.AppMenus
                    .IgnoreQueryFilters()
                    .Where(m => ids.Contains(m.Id))
                    .ExecuteDeleteAsync(ct);

                deleted += affected;

                if (affected < layer.Count)
                {
                    // Another replica purged some or all of this layer first — benign.
                    logger.LogDebug("[Menus] orphan purge partial race — {Affected}/{Expected} rows deleted",
                        affected, layer.Count);
                    if (affected == 0)
                    {
                        raced = true;
                        break;
                    }
                }
            }
            catch (DbUpdateException ex)
            {
                // Structural conflict: e.g. a desired menu still points at one of these as its parent
                // (would only happen on a real bug in BuildMenuList). Surface it loudly; leave the
                // rest for the next boot.
                logger.LogWarning(ex, "[Menus] orphan purge batch failed ({Count} rows) — leaving for next boot", layer.Count);
                break;
            }
        }

        if (deleted < orphans.Count && !raced)
            logger.LogError("[Menus] purged {Done}/{Total} orphan menu(s); {Left} left (parent cycle or FK conflict) — retry next boot",
                deleted, orphans.Count, orphans.Count - deleted);

        return deleted;
    }

    /// <summary>True if <paramref name="publicId"/> is in this seeder's pinned PublicId family.</summary>
    internal static bool IsSeededMenuFamily(Guid publicId)
        => publicId.ToString().StartsWith(SeededMenuPublicIdPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Partitions orphan rows into deletion layers, leaves first: layer 0 is rows referenced by no
    /// other orphan as a parent; layer 1 is rows that become unreferenced once layer 0 is removed; etc.
    /// Deleting layer-by-layer (a <c>SaveChanges</c> per layer) satisfies the <c>AppMenu.ParentId</c>
    /// self-reference (<see cref="Microsoft.EntityFrameworkCore.DeleteBehavior.Restrict"/>) without
    /// relying on EF's batch ordering. Rows in a parent cycle (impossible for a real menu tree) appear
    /// in no layer — the caller surfaces the shortfall.
    /// </summary>
    internal static List<List<T>> OrderOrphanMenusLeafFirst<T>(
        IReadOnlyCollection<T> orphans, Func<T, Guid> publicId, Func<T, Guid?> parentId)
    {
        var layers = new List<List<T>>();
        var pending = new List<T>(orphans);
        while (pending.Count > 0)
        {
            var referencedAsParent = pending
                .Select(parentId).Where(p => p.HasValue).Select(p => p!.Value)
                .ToHashSet();

            var leaves = pending.Where(m => !referencedAsParent.Contains(publicId(m))).ToList();
            if (leaves.Count == 0) break; // cyclic remainder — caller logs the gap

            layers.Add(leaves);
            foreach (var l in leaves) pending.Remove(l);
        }
        return layers;
    }

    private static List<AppMenu> BuildMenuList()
    {
        int s = 0; // top-level SortOrder counter
        return
        [
            // Top-level #1: Nghiep vu (group header)
            M(MenuGrpBusinessId, null, "group-business", "Nghiệp vụ", null, null, s++),
            M(MenuDashboardId,   MenuGrpBusinessId, "dashboard",   "Tổng quan",   "DashboardOutlined",   "/",           1),
            M(MenuInboxId,       MenuGrpBusinessId, "inbox",       "Hộp thư",     "InboxOutlined",       "/inbox",      3),
            M(MenuFormsId,       MenuGrpBusinessId, "forms",       "Biểu mẫu",    "FormOutlined",        "/forms",      4),
            M(MenuFilesId,       MenuGrpBusinessId, "files",       "Tài liệu",    "FolderOutlined",      "/files",      5),

            // Top-level #2: AI & Tro ly (group header)
            M(MenuGrpAiAssistantId, null, "group-ai-assistant", "AI & Trợ lý", null, null, s++),
            M(MenuAiSearchId, MenuGrpAiAssistantId, "ai-search", "AI Tìm kiếm", "RobotOutlined",        "/ai/search", 1),
            M(MenuCopilotId,  MenuGrpAiAssistantId, "copilot",   "Copilot",     "ThunderboltOutlined",  "/copilot",   2),
            M(MenuChatId,     MenuGrpAiAssistantId, "chat",      "Chat",        "MessageOutlined",      "/chat",      3),

            // Top-level #3: Tich hop & Bao cao (group header)
            M(MenuGrpIntegrationReportsId, null, "group-integration-reports", "Tích hợp & Báo cáo", null, null, s++),

            // 3a. Integration submenu
            M(MenuIntegrationId,  MenuGrpIntegrationReportsId, "integration",  "Tích hợp",       "ApiOutlined",      null, 1),
            M(MenuIntPartnersId,  MenuIntegrationId, "int-partners",  "Đối tác",                "TeamOutlined",     "/integration/partners",     1),
            M(MenuIntContractsId, MenuIntegrationId, "int-contracts", "Hợp đồng",               "FileTextOutlined", "/integration/contracts",    2),
            M(MenuIntMsgLogsId,   MenuIntegrationId, "int-msg-logs",  "Log tích hợp",           "MessageOutlined",  "/integration/message-logs", 3),

            // 3b. Audit (leaf inside group)
            M(MenuAuditId, MenuGrpIntegrationReportsId, "audit", "Kiểm toán", "AuditOutlined", "/audit/logs", 2),

            // 3c. Reporting submenu
            M(MenuReportingId,        MenuGrpIntegrationReportsId, "reporting",          "Báo cáo",       "BarChartOutlined", null,                    3),
            M(MenuReportDefinitionsId,MenuReportingId,             "report-definitions", "Mẫu báo cáo",   "FileTextOutlined", "/reports/definitions",  1),
            M(MenuReportExecutionsId, MenuReportingId,             "report-executions",  "Lịch sử chạy",  "HistoryOutlined",  "/reports/executions",   2),

            // Top-level #4: Dinh danh & Quyen (admin submenu)
            M(MenuGrpIdentityId, null, "admin-identity", "Định danh & Quyền", "TeamOutlined", null, s++),
            M(MenuUsersId,       MenuGrpIdentityId, "admin-users",   "Người dùng",         "TeamOutlined",            "/admin/users",         1, "Quản lý người dùng và thông tin cá nhân"),
            M(MenuRolesId,       MenuGrpIdentityId, "roles",         "Vai trò",            "SafetyCertificateOutlined","/roles",              2),
            M(MenuGroupsId,      MenuGrpIdentityId, "admin-groups",  "Nhóm",               "UsergroupAddOutlined",    "/admin/groups",        3),
            M(MenuExtIdsId,      MenuGrpIdentityId, "admin-ext-ids", "Danh tính ngoài",    "LinkOutlined",            "/admin/external-identities", 4),
            M(MenuJitId,         MenuGrpIdentityId, "admin-jit",     "JIT Providers",      "ApiOutlined",             "/admin/jit-provider-configs",5),
            M(MenuAbacId,        MenuGrpIdentityId, "admin-abac",    "Quy tắc ABAC",       "SafetyCertificateOutlined","/admin/abac-rules",   6),
            M(MenuMfaPoliciesId, MenuGrpIdentityId, "admin-mfa-pol", "Chính sách MFA",     "SafetyCertificateOutlined","/admin/mfa-policies", 7),
            // M(MenuSodId,         MenuGrpIdentityId, "admin-sod",     "Quy tac SoD", ...), // tam an
            // M(MenuPolicyRulesId, MenuGrpIdentityId, "admin-policy",  "Chinh sach quyen", ...), // tam an
            M(MenuCredPolId,     MenuGrpIdentityId, "admin-cred-pol","Chính sách mật khẩu","LockOutlined",           "/admin/credential-policies", 9),
            // M(MenuAccessRevId,   MenuGrpIdentityId, "admin-access-rev","Ra soat quyen", ...), // tam an
            M(MenuDelegId,       MenuGrpIdentityId, "admin-deleg",   "Ủy quyền",           "SwapOutlined",            "/admin/delegations",  11),
            M(MenuSessionsId,    MenuGrpIdentityId, "admin-sessions","Phiên làm việc",     "DesktopOutlined",         "/admin/sessions",     12),
            M(MenuDataScopesId,  MenuGrpIdentityId, "admin-data-scopes","Phạm vi dữ liệu","EyeOutlined",              "/admin/data-scopes",  13, "Cấu hình ABAC — phạm vi dữ liệu user được truy cập"),

            // Top-level #5: Noi dung & Quy trinh (admin submenu)
            M(MenuGrpContentId,  null, "admin-content", "Nội dung & Quy trình", "NodeIndexOutlined", null, s++),
            M(MenuWorkflowId,    MenuGrpContentId, "admin-workflow", "Quy trình",       "NodeIndexOutlined", "/admin/workflow",              1),
            M(MenuRulesId,       MenuGrpContentId, "admin-rules",    "Bộ quy tắc",      "EditOutlined",      "/admin/rules",                 2),
            M(MenuTemplatesId,   MenuGrpContentId, "admin-templates","Mẫu tài liệu",    "FileProtectOutlined","/admin/templates",            3),
            M(MenuNotifTplId,    MenuGrpContentId, "admin-notif-tpl","Mẫu thông báo",   "BellOutlined",      "/admin/notification-templates",4),
            M(MenuMenusId,       MenuGrpContentId, "admin-menus",    "Menu",             "MenuOutlined",      "/admin/menus",                 5),
            M(MenuBusinessProcessesId, MenuGrpContentId, "admin-business-processes", "Quy trình nghiệp vụ", "ApartmentOutlined", "/admin/business-processes", 6),

            // Top-level #6: He thong (admin submenu)
            M(MenuGrpSystemId,   null, "admin-system", "Hệ thống", "SettingOutlined", null, s++),
            M(MenuTenantsId,     MenuGrpSystemId, "admin-tenants",  "Quản lý tenant",    "GlobalOutlined",    "/admin/tenants",       1, "Quản trị tenant — chỉ SystemAdmin"),
            M(MenuSysParamsId,   MenuGrpSystemId, "admin-sys-params","Tham số hệ thống","SettingOutlined",    "/admin/system-params", 2),
            M(MenuMasterDataId,  MenuGrpSystemId, "admin-master",   "Danh mục",          "DatabaseOutlined",  "/admin/master-data",   3),
            M(MenuOrgId,         MenuGrpSystemId, "admin-org",      "Tổ chức",           "ApartmentOutlined", "/admin/organization",  4),
            M(MenuJobsId,        MenuGrpSystemId, "admin-jobs",     "Công việc nền",     "ScheduleOutlined",  "/admin/jobs",          5),
            M(MenuHealthId,      MenuGrpSystemId, "admin-health",   "Health Check",      "HeartOutlined",     "/admin/health",        6),
            M(MenuBackupId,      MenuGrpSystemId, "admin-backup",   "Sao lưu",           "CloudUploadOutlined","/admin/backup",       7),
            // M(MenuRtbfId,        MenuGrpSystemId, "admin-rtbf",    "Xoa du lieu RTBF", ...), // tam an

            // Top-level #7: API & Webhooks (admin submenu)
            M(MenuGrpApiWebhooksId, null, "admin-api-webhooks", "API & Webhooks", "KeyOutlined", null, s++),
            M(MenuApiKeysId,  MenuGrpApiWebhooksId, "admin-api-keys", "API Keys",         "KeyOutlined", "/admin/api-keys", 1, "Cấp API key cho app/đối tác gọi vào hệ thống"),
            M(MenuWebhooksId, MenuGrpApiWebhooksId, "admin-webhooks", "Thông báo gửi đi", "ApiOutlined", "/admin/webhooks", 2, "Log thông báo hệ thống gửi tới subscriber"),

            // Top-level #8: AI & Automation (admin submenu)
            M(MenuGrpAiAutomationId, null, "admin-ai-automation", "AI & Automation", "ExperimentOutlined", null, s++),
            M(MenuAiAdminId, MenuGrpAiAutomationId, "admin-ai", "AI Admin", "ExperimentOutlined", "/admin/ai", 1, "Cấu hình model LLM, prompt, embedding"),

            // Top-level #9: Bao mat & Tuan thu (Phase-05 QĐ 742 + phase-05b) — mirror FE getSecurityMenuChildren()
            M(MenuGrpSecurityId,        null, "admin-security", "Bảo mật & Tuân thủ", "SafetyOutlined", null, s++),
            M(MenuIpWhitelistId,        MenuGrpSecurityId, "admin-ip-whitelist",   "IP whitelist",       "SafetyOutlined",     "/admin/ip-whitelist",    1),
            M(MenuAuditPolicyId,        MenuGrpSecurityId, "admin-audit-policy",   "Chính sách audit",   "AuditOutlined",      "/admin/audit-policy",    2),
            M(MenuBackupPolicyId,       MenuGrpSecurityId, "admin-backup-policy",  "Chính sách sao lưu", "CloudUploadOutlined","/admin/backup-policy",   3),
            M(MenuSignatureConfigId,    MenuGrpSecurityId, "admin-signature-config","Cấu hình chữ ký số","SignatureOutlined", "/admin/signature-config",4),
            // Phase-06 Slice C — self-service signing-cert enrollment approval queue. Gated by the dedicated
            // SIGNATURE.ENROLLMENT.APPROVE below; was missing from the DB menu tree entirely (route + page
            // shipped, but no AppMenu row → the API-driven sidebar never surfaced it).
            M(MenuSignatureEnrollmentsId, MenuGrpSecurityId, "admin-signature-enrollments","Duyệt chứng thư số","SafetyCertificateOutlined","/admin/signature-enrollments",5),
            // Security Health submenu container (route=null) — gates by SECURITY.HEALTH.VIEW; 6 child leaves inherit
            M(MenuGrpSecurityHealthId,  MenuGrpSecurityId, "admin-security-health","Sức khỏe bảo mật",   "MedicineBoxOutlined", null,                    6),
            M(MenuSecHlthLoginPolicyId,    MenuGrpSecurityHealthId, "admin-sec-hlth-login",   "Chính sách đăng nhập", "LockOutlined",            "/admin/security-health/login-policy",     1),
            M(MenuSecHlthPasswordPolicyId, MenuGrpSecurityHealthId, "admin-sec-hlth-passwd",  "Chính sách mật khẩu",  "SafetyCertificateOutlined","/admin/security-health/password-policy",  2),
            M(MenuSecHlthAuditRetentionId, MenuGrpSecurityHealthId, "admin-sec-hlth-aud-ret", "Lưu giữ audit",        "AuditOutlined",           "/admin/security-health/audit-retention",  3),
            M(MenuSecHlthAuditIntegrityId, MenuGrpSecurityHealthId, "admin-sec-hlth-aud-int", "Toàn vẹn audit",       "SafetyOutlined",          "/admin/security-health/audit-integrity",  4),
            M(MenuSecHlthKeyRotationId,    MenuGrpSecurityHealthId, "admin-sec-hlth-keyrot",  "Xoay khóa",            "KeyOutlined",             "/admin/security-health/key-rotation",     5),
            M(MenuSecHlthBackupHistoryId,  MenuGrpSecurityHealthId, "admin-sec-hlth-backhist","Lịch sử sao lưu",      "CloudUploadOutlined",     "/admin/security-health/backup-history",   6),

            // Top-level #10 REMOVED 2026-05-14: "Doi tac tich hop" admin alias dropped — visually
            // duplicated the business chain "Tich hop & Bao cao > Tich hop > Doi tac". Partners page
            // reachable via that chain (same PartnerListPage component, same INTEG.PARTNER.VIEW gate).

            // Top-level #11 REMOVED 2026-05-11: "Tong quan admin" → /admin route deleted from FE.
            // Orphan row purged from DB; this section intentionally left without seeding admin-overview.
            // If a new admin landing route is added, re-introduce here with the updated path.
        ];
    }

    // Returns tuples (MenuPublicId, PermCode) — resolved to long MenuId in SeedMenusAsync.
    private static List<(Guid MenuPublicId, string PermCode)> BuildMenuPermissions()
    {
        return
        [
            // Top-level group headers (Nghiep vu / AI / Tich hop & Bao cao) — no permission (public containers)

            // Business group children (public: Dashboard, Inbox — no perms)
            P(MenuFormsId,       "FORMS.SUBMIT.VIEW"),
            P(MenuFilesId,       "FILES.FILE.VIEW"),

            // AI group children (public: Copilot, Chat — no perms)
            P(MenuAiSearchId,    "AI.EXTRACT.USE"),
            P(MenuChatId,        "COLLAB.CONV.VIEW"),

            // Integration & Reports group children
            P(MenuIntegrationId, "INTEG.CONTRACT.VIEW"),
            P(MenuIntPartnersId, "INTEG.PARTNER.VIEW"),
            P(MenuIntContractsId,"INTEG.CONTRACT.VIEW"),
            P(MenuIntMsgLogsId,  "INTEG.MSGLOG.VIEW"),
            P(MenuRolesId,       "IDENTITY.ROLE.VIEW"),
            P(MenuAuditId,       "AUDIT.LOG.VIEW"),
            P(MenuReportingId,           "REPORT.DASH.VIEW"),
            P(MenuReportDefinitionsId,   "REPORT.REPORT.VIEW"),
            P(MenuReportExecutionsId,    "REPORT.REPORT.VIEW"),

            // Admin top-level submenus (Option C) — each gates by IDENTITY.USER.VIEW
            P(MenuGrpIdentityId,           "IDENTITY.USER.VIEW"),
            P(MenuGrpContentId,            "IDENTITY.USER.VIEW"),
            P(MenuGrpSystemId,             "IDENTITY.USER.VIEW"),
            P(MenuGrpApiWebhooksId,        "IDENTITY.USER.VIEW"),
            P(MenuGrpAiAutomationId,       "IDENTITY.USER.VIEW"),
            // SECURITY group gates by IP_RULE.VIEW (most common SECURITY perm); children have their own wires.
            P(MenuGrpSecurityId,           "SECURITY.IP_RULE.VIEW"),
            // P(MenuGrpPartnerIntegrationId, ...) REMOVED 2026-05-14 with admin alias chain.
            // P(MenuAdminOverviewId, ...) REMOVED 2026-05-11 with admin-overview menu row.

            // Admin > Identity group children
            P(MenuUsersId,      "IDENTITY.USER.VIEW"),
            P(MenuGroupsId,     "IDENTITY.GROUP.VIEW"),
            P(MenuExtIdsId,     "IDENTITY.USER.VIEW"),
            P(MenuJitId,        "IDENTITY.POLICY.VIEW"),
            P(MenuAbacId,       "IDENTITY.ABAC.VIEW"),
            P(MenuMfaPoliciesId,"SECURITY.MFA_POLICY.VIEW"),
            // P(MenuSodId, ...), // tam an
            // P(MenuPolicyRulesId, ...), // tam an
            P(MenuCredPolId,    "IDENTITY.POLICY.VIEW"),
            // P(MenuAccessRevId, ...), // tam an
            P(MenuDelegId,      "IDENTITY.DELEG.VIEW"),
            P(MenuSessionsId,   "IDENTITY.SESSION.VIEW"),
            P(MenuDataScopesId, "IDENTITY.DATASCOPE.MANAGE"),

            // Admin > Content group children
            P(MenuWorkflowId,   "WORKFLOW.DEF.VIEW"),
            P(MenuRulesId,      "RULES.RULESET.VIEW"),
            P(MenuTemplatesId,  "FILES.TEMPLATE.VIEW"),
            P(MenuNotifTplId,   "NOTIF.TEMPLATE.VIEW"),
            P(MenuMenusId,      "IDENTITY.MENU.VIEW"),
            P(MenuBusinessProcessesId, "BUSINESSPROCESS.BP.VIEW"),

            // Admin > System group children
            P(MenuTenantsId,    "IDENTITY.TENANT.VIEW"),
            P(MenuSysParamsId,  "SYSPARAM.PARAM.VIEW"),
            P(MenuMasterDataId, "MASTER.DATA.VIEW"),
            P(MenuOrgId,        "ORG.UNIT.VIEW"),
            P(MenuJobsId,       "SYSTEM.CONFIG.VIEW"),
            P(MenuHealthId,     "SYSTEM.CONFIG.VIEW"),
            P(MenuBackupId,     "SYSTEM.CONFIG.MANAGE"),
            // P(MenuRtbfId, ...), // tam an

            // Admin > API & Webhooks group children
            P(MenuApiKeysId,    "INTEG.CONTRACT.VIEW"),
            P(MenuWebhooksId,   "INTEG.CONTRACT.VIEW"),

            // Admin > AI & Automation group children
            P(MenuAiAdminId,    "AI.MODEL.VIEW"),

            // Admin > Security & Compliance group children (Phase-05 QĐ 742 + phase-05b)
            P(MenuIpWhitelistId,        "SECURITY.IP_RULE.VIEW"),
            P(MenuAuditPolicyId,        "AUDIT.RETENTION.VIEW"),
            P(MenuBackupPolicyId,       "SYSTEM.CONFIG.MANAGE"),
            P(MenuSignatureConfigId,    "SECURITY.SIGNATURE_CONFIG.VIEW"),
            P(MenuSignatureEnrollmentsId, "SIGNATURE.ENROLLMENT.APPROVE"),
            // Health submenu container gates 6 leaves via SECURITY.HEALTH.VIEW (cascade pattern)
            P(MenuGrpSecurityHealthId,  "SECURITY.HEALTH.VIEW"),

            // Admin > Partner Integration group children — REMOVED 2026-05-14 with admin alias chain.
        ];
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an AppMenu with a pinned PublicId for E2E test stability.
    /// AppMenu is T2: long Id (DB-assigned) + Guid PublicId (pinned stable AK).
    /// ParentId is Guid? referencing parent's PublicId (not long Id).
    /// </summary>
    private static AppMenu M(
        Guid publicId, Guid? parentPublicId,
        string code, string title, string? icon, string? route,
        int sort, string? desc = null)
        => AppMenu.Create(
            gen: new PinnedGuidGenerator(publicId),
            code: code,
            title: title,
            parentId: parentPublicId,
            description: desc,
            icon: icon,
            route: route,
            sortOrder: sort,
            tenantId: null, // System menus — all tenants
            actorId: Guid.Empty);

    /// <summary>Returns (MenuPublicId, PermCode) tuple — resolved to MenuRolePermission after save.</summary>
    private static (Guid MenuPublicId, string PermCode) P(Guid menuPublicId, string permCode)
        => (menuPublicId, permCode);

    /// <summary>Flush Redis menu-tree:* keys after seeding [RT-06].</summary>
    private async Task FlushMenuCacheAsync(IServiceProvider sp)
    {
        try
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var server = redis.GetServer(redis.GetEndPoints().First());
            var db = redis.GetDatabase();
            var keys = server.Keys(pattern: "menu-tree:*").ToArray();
            if (keys.Length > 0)
                await db.KeyDeleteAsync(keys);
            logger.LogInformation("Flushed {Count} menu-tree cache keys after seed", keys.Length);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis unavailable for menu cache flush — cache will expire in 30min");
        }
    }
}
