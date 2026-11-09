using AqtCoreFW.MasterData.Domain.Constants;
using AqtCoreFW.MasterData.Domain.Entities;
using AqtCoreFW.MasterData.Domain.Enums;
using AqtCoreFW.MasterData.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.MasterData.Infrastructure.Services;

/// <summary>
/// Seed record for WorkflowStateTaxonomy baseline rows (phase-00 plan §Seed Data).
/// </summary>
public sealed record WorkflowStateTaxonomySeed(
    string Code,
    string NameVi,
    string NameEn,
    string Icon,
    string Color,
    WorkflowStateCategory Category,
    int SortOrder);

/// <summary>
/// Seed record for WorkflowActionTaxonomy baseline rows.
/// </summary>
public sealed record WorkflowActionTaxonomySeed(
    string Code,
    string NameVi,
    string NameEn,
    string Icon,
    int SortOrder);

/// <summary>
/// Phase-00 workflow taxonomy seeder — R2 strict semantics.
///
/// Contract (critical):
/// - INSERT-IF-NOT-EXISTS matched by Code (case-insensitive via default CI_AS collation).
/// - On existing Code match: NEVER update, NEVER resurrect soft-deleted row.
/// - Uses IgnoreQueryFilters() so hard-deleted rows (IsDeleted=true) are visible —
///   prevents unique-constraint violation on startup reinsert.
///
/// Rationale: admin may deprecate a code via soft-delete (IsActive=false). On app restart,
/// the seeder must NOT resurrect the code. See phase-00 §Non-Functional Seeder Semantics R2.
/// </summary>
public static class WorkflowTaxonomySeeder
{
    public static async Task SeedStatesAsync(
        MasterDataDbContext db,
        IGuidGenerator guidGen,
        IEnumerable<WorkflowStateTaxonomySeed> baseline,
        CancellationToken ct)
    {
        foreach (var seed in baseline)
        {
            // IgnoreQueryFilters: see hard-deleted rows too (they'd still collide on unique Code index)
            var exists = await db.WorkflowStateTaxonomies
                .IgnoreQueryFilters()
                .AnyAsync(x => x.Code == seed.Code, ct);

            if (exists) continue; // R2 strict — never touch existing

            db.WorkflowStateTaxonomies.Add(WorkflowStateTaxonomy.Create(
                guidGen,
                code: seed.Code,
                nameVi: seed.NameVi,
                nameEn: seed.NameEn,
                category: seed.Category,
                sortOrder: seed.SortOrder,
                icon: seed.Icon,
                color: seed.Color));
        }

        await db.SaveChangesAsync(ct);
    }

    public static async Task SeedActionsAsync(
        MasterDataDbContext db,
        IGuidGenerator guidGen,
        IEnumerable<WorkflowActionTaxonomySeed> baseline,
        CancellationToken ct)
    {
        foreach (var seed in baseline)
        {
            var exists = await db.WorkflowActionTaxonomies
                .IgnoreQueryFilters()
                .AnyAsync(x => x.Code == seed.Code, ct);

            if (exists) continue;

            db.WorkflowActionTaxonomies.Add(WorkflowActionTaxonomy.Create(
                guidGen,
                code: seed.Code,
                nameVi: seed.NameVi,
                nameEn: seed.NameEn,
                sortOrder: seed.SortOrder,
                icon: seed.Icon));
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// 10 baseline state codes per plan §Seed Data (R10 kept Returned + phase-01 added
    /// ReturnedForRevision for legacy Cases parity — see WorkflowTaxonomyCodes.WorkflowStates).
    /// Seeder is R2 strict INSERT-IF-NOT-EXISTS; adding rows here is safe on existing DB
    /// because previously-seeded 9 rows stay untouched and only the new code gets inserted.
    /// </summary>
    public static IReadOnlyList<WorkflowStateTaxonomySeed> BaselineStates { get; } =
    [
        new(WorkflowTaxonomyCodes.WorkflowStates.Draft,
            "Nháp", "Draft", "EditOutlined", "#808080",
            WorkflowStateCategory.Initial, SortOrder: 10),
        new(WorkflowTaxonomyCodes.WorkflowStates.Submitted,
            "Đã nộp", "Submitted", "SendOutlined", "#1890FF",
            WorkflowStateCategory.Intermediate, SortOrder: 20),
        new(WorkflowTaxonomyCodes.WorkflowStates.UnderReview,
            "Đang xử lý", "Under Review", "AuditOutlined", "#FA8C16",
            WorkflowStateCategory.Intermediate, SortOrder: 30),
        new(WorkflowTaxonomyCodes.WorkflowStates.Assigned,
            "Đã phân công", "Assigned", "UserOutlined", "#722ED1",
            WorkflowStateCategory.Intermediate, SortOrder: 40),
        new(WorkflowTaxonomyCodes.WorkflowStates.Approved,
            "Đã duyệt", "Approved", "CheckCircleOutlined", "#52C41A",
            WorkflowStateCategory.Terminal, SortOrder: 50),
        new(WorkflowTaxonomyCodes.WorkflowStates.Rejected,
            "Từ chối", "Rejected", "CloseCircleOutlined", "#F5222D",
            WorkflowStateCategory.Rejected, SortOrder: 60),
        new(WorkflowTaxonomyCodes.WorkflowStates.Returned,
            "Yêu cầu bổ sung", "Returned for Revision", "RollbackOutlined", "#FAAD14",
            WorkflowStateCategory.Intermediate, SortOrder: 70),
        new(WorkflowTaxonomyCodes.WorkflowStates.ReturnedForRevision,
            "Trả lại để chỉnh sửa", "Returned for Revision (legacy)", "RollbackOutlined", "#FAAD14",
            WorkflowStateCategory.Intermediate, SortOrder: 75),
        new(WorkflowTaxonomyCodes.WorkflowStates.Closed,
            "Đã đóng", "Closed", "LockOutlined", "#8C8C8C",
            WorkflowStateCategory.Terminal, SortOrder: 80),
        new(WorkflowTaxonomyCodes.WorkflowStates.Cancelled,
            "Đã huỷ", "Cancelled", "StopOutlined", "#595959",
            WorkflowStateCategory.Rejected, SortOrder: 90),
    ];

    /// <summary>9 baseline action codes per plan §Seed Data (unchanged from round 1).</summary>
    public static IReadOnlyList<WorkflowActionTaxonomySeed> BaselineActions { get; } =
    [
        new(WorkflowTaxonomyCodes.WorkflowActions.Submit,   "Nộp", "Submit", "SendOutlined", SortOrder: 10),
        new(WorkflowTaxonomyCodes.WorkflowActions.Assign,   "Phân công", "Assign", "UserOutlined", SortOrder: 20),
        new(WorkflowTaxonomyCodes.WorkflowActions.Approve,  "Duyệt", "Approve", "CheckOutlined", SortOrder: 30),
        new(WorkflowTaxonomyCodes.WorkflowActions.Reject,   "Từ chối", "Reject", "CloseOutlined", SortOrder: 40),
        new(WorkflowTaxonomyCodes.WorkflowActions.Return,   "Yêu cầu bổ sung", "Return", "RollbackOutlined", SortOrder: 50),
        new(WorkflowTaxonomyCodes.WorkflowActions.Close,    "Đóng", "Close", "LockOutlined", SortOrder: 60),
        new(WorkflowTaxonomyCodes.WorkflowActions.Cancel,   "Huỷ", "Cancel", "StopOutlined", SortOrder: 70),
        new(WorkflowTaxonomyCodes.WorkflowActions.Reopen,   "Mở lại", "Reopen", "UnlockOutlined", SortOrder: 80),
        new(WorkflowTaxonomyCodes.WorkflowActions.Reassign, "Phân công lại", "Reassign", "SwapOutlined", SortOrder: 90),
    ];
}
