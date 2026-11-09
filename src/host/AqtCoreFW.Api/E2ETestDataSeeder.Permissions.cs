using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Api;

/// <summary>
/// Partial: seeds all fine-grained permissions for RBAC across GOV modules.
/// Idempotent: skips if any permissions already exist.
/// Seeds the full PermCodes catalog (delta-merge — INSERTs missing rows on existing dev DBs).
/// Codes MUST match PermCodes constants. Drift between PermCodes.All and BuildPermissionList()
/// causes 403 on new endpoints (e.g. T5-10 WORKFLOW.MIGRATION.*).
/// </summary>
public sealed partial class E2ETestDataSeeder
{
    private static async Task SeedPermissionsAsync(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<IdentityDbContext>();

        // Per-row idempotency (delta-merge): only insert codes not already in DB.
        // Replaces "skip-if-any" which prevented new perms from seeding on existing dev DBs.
        var existingCodes = (await db.Permissions
            .Select(p => p.Code)
            .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        var missing = BuildPermissionList()
            .Where(p => !existingCodes.Contains(p.Code))
            .ToList();

        if (missing.Count == 0) return;

        await db.Permissions.AddRangeAsync(missing, ct);
        await db.SaveChangesAsync(ct);
    }

    internal static List<Permission> BuildPermissionList()
    {
        var perms = new List<Permission>();

        // IDENTITY module (23)
        AddPerms(perms, "IDENTITY", "USER",      ["VIEW", "CREATE", "UPDATE"], ["DELETE"]);
        AddPerms(perms, "IDENTITY", "ROLE",      ["VIEW"],                     ["MANAGE"]);
        AddPerms(perms, "IDENTITY", "PERM",      [],                           ["ASSIGN"]);
        AddPerms(perms, "IDENTITY", "GROUP",     ["VIEW", "MANAGE"],           []);
        AddPerms(perms, "IDENTITY", "DELEG",     ["VIEW"],                     ["MANAGE"]);
        AddPerms(perms, "IDENTITY", "ABAC",      ["VIEW"],                     ["MANAGE"]);
        AddPerms(perms, "IDENTITY", "SOD",       ["VIEW", "VIEW_AUDIT_LOG", "EXPORT_AUDIT_LOG"], ["MANAGE"]);
        AddPerms(perms, "IDENTITY", "SESSION",   ["VIEW"],                     ["REVOKE"]);
        AddPerms(perms, "IDENTITY", "MENU",      ["VIEW", "MANAGE"],           []);
        AddPerms(perms, "IDENTITY", "POLICY",    ["VIEW"],                     ["MANAGE"]);
        AddPerms(perms, "IDENTITY", "CONSENT",   ["VIEW"],                     []);
        AddPerms(perms, "IDENTITY", "DATASCOPE", [],                           ["MANAGE"]);
        AddPerms(perms, "IDENTITY", "TENANT",    ["VIEW"],                     ["MANAGE"]);

        // FILES module (8)
        AddPerms(perms, "FILES", "FILE",      ["VIEW", "UPLOAD", "DOWNLOAD"], ["DELETE"]);
        AddPerms(perms, "FILES", "TEMPLATE",  ["VIEW", "MANAGE"],             []);
        AddPerms(perms, "FILES", "RETENTION", ["VIEW"],                       ["MANAGE"]);

        // AUDIT module (9: +2 RETENTION.* — phase-01 slice-D2)
        AddPerms(perms, "AUDIT", "LOG",       ["VIEW", "EXPORT"], []);
        AddPerms(perms, "AUDIT", "LOGIN",     ["VIEW"],           []);
        AddPerms(perms, "AUDIT", "RTBF",      ["VIEW"],           ["EXECUTE"]);
        AddPerms(perms, "AUDIT", "INCIDENT",  ["VIEW"],           ["MANAGE"]);
        AddPerms(perms, "AUDIT", "RETENTION", ["VIEW"],           ["MANAGE"]);

        // WORKFLOW module (14: +1 INSTANCE.MANAGE — phase-04-A follow-up 2026-05-22 4-A-A invariant closure)
        AddPerms(perms, "WORKFLOW", "DEF",        ["VIEW", "MANAGE"],     []);
        AddPerms(perms, "WORKFLOW", "TASK",       ["VIEW", "PROCESS"],    []);
        AddPerms(perms, "WORKFLOW", "INSTANCE",   ["VIEW"],               ["MANAGE"]);
        AddPerms(perms, "WORKFLOW", "ASSIGN",     ["MANAGE"],             []);
        AddPerms(perms, "WORKFLOW", "DELEGATION", [],                     ["MANAGE"]);
        AddPerms(perms, "WORKFLOW", "ANALYTICS",  ["VIEW"],               []);
        AddPerms(perms, "WORKFLOW", "MIGRATION",  ["READ"],               ["CREATE", "APPROVE", "EXECUTE", "CANCEL"]);

        // FORMS module (9: +APPROVE/REJECT — were missing, controllers reference both policies;
        // +WFLINK.MANAGE — P6-DEFER-1 phase 05, admin orphan-workflow-link retry/shelve, least-privilege)
        AddPerms(perms, "FORMS", "TEMPLATE", ["VIEW", "MANAGE"],        []);
        AddPerms(perms, "FORMS", "SUBMIT",   ["VIEW", "CREATE", "EXPORT"], ["APPROVE", "REJECT"]);
        AddPerms(perms, "FORMS", "VIEW",     ["MANAGE"],                []);
        AddPerms(perms, "FORMS", "WFLINK",   [],                        ["MANAGE"]);

        // NOTIFICATIONS module (4)
        AddPerms(perms, "NOTIF", "MSG",      ["VIEW", "SEND"],    []);
        AddPerms(perms, "NOTIF", "TEMPLATE", ["VIEW", "MANAGE"],  []);

        // ORGANIZATION module (4)
        AddPerms(perms, "ORG", "UNIT",  ["VIEW", "MANAGE"], []);
        AddPerms(perms, "ORG", "STAFF", ["VIEW", "MANAGE"], []);

        // MASTERDATA module (6 — phase-00: +WORKFLOW_TAXONOMY.VIEW)
        AddPerms(perms, "MASTER", "DATA",              ["VIEW", "MANAGE"], []);
        AddPerms(perms, "MASTER", "DICT",              ["VIEW", "MANAGE"], []);
        AddPerms(perms, "MASTER", "EXTMAP",            ["MANAGE"],         []);
        AddPerms(perms, "MASTER", "WORKFLOW_TAXONOMY", ["VIEW"],           []);

        // PLATFORM module (1 — phase-00: new platform-admin tier, SystemAdmin only)
        AddPerms(perms, "PLATFORM", "WORKFLOW_TAXONOMY", [], ["MANAGE"]);

        // AI module (5)
        AddPerms(perms, "AI", "EXTRACT", ["USE"],           []);
        AddPerms(perms, "AI", "MODEL",   ["VIEW"],          ["MANAGE"]);
        AddPerms(perms, "AI", "PROMPT",  ["VIEW", "MANAGE"],[]);

        // COLLABORATION module (3)
        AddPerms(perms, "COLLAB", "CONV", ["VIEW", "CREATE"], []);
        AddPerms(perms, "COLLAB", "MSG",  ["SEND"],           []);

        // INTEGRATION module (5)
        AddPerms(perms, "INTEG", "CONTRACT", ["VIEW", "MANAGE"], []);
        AddPerms(perms, "INTEG", "PARTNER",  ["VIEW", "MANAGE"], []);
        AddPerms(perms, "INTEG", "MSGLOG",   ["VIEW"],           []);

        // REPORTING module (5)
        AddPerms(perms, "REPORT", "DASH",   ["VIEW", "MANAGE"],  []);
        AddPerms(perms, "REPORT", "REPORT", ["VIEW", "EXPORT", "MANAGE"],  []);
        AddPerms(perms, "REPORT", "WIDGET", ["MANAGE"],           []);

        // RULES module (5)
        AddPerms(perms, "RULES", "RULESET", ["VIEW", "MANAGE"], []);
        AddPerms(perms, "RULES", "DTABLE",  ["VIEW", "MANAGE"], []);
        AddPerms(perms, "RULES", "EVAL",    ["USE"],            []);

        // SEARCH module (2)
        AddPerms(perms, "SEARCH", "QUERY", ["USE"],    []);
        AddPerms(perms, "SEARCH", "SAVED", ["MANAGE"], []);

        // SYSTEMPARAMS module (6)
        AddPerms(perms, "SYSPARAM", "PARAM",    ["VIEW"],          ["MANAGE"]);
        AddPerms(perms, "SYSPARAM", "FLAG",     ["VIEW"],          ["MANAGE"]);
        AddPerms(perms, "SYSPARAM", "ANNOUNCE", ["VIEW", "MANAGE"],[]);

        // SYSTEM ADMIN (2)
        AddPerms(perms, "SYSTEM", "CONFIG", ["VIEW"], ["MANAGE"]);

        // SECURITY module (9 — phase-03 QĐ742 1.6.a: MFA policy + emergency; phase-05 §2.2.a: IP rule; phase-05 Wave-2 D7: health; phase-05b W6 §5.1: signature config)
        AddPerms(perms, "SECURITY", "MFA_POLICY",       ["VIEW"], ["MANAGE"]);
        AddPerms(perms, "SECURITY", "MFA_EMERGENCY",    [],       ["REQUEST", "APPROVE"]);
        AddPerms(perms, "SECURITY", "IP_RULE",          ["VIEW"], ["MANAGE"]);
        AddPerms(perms, "SECURITY", "HEALTH",           ["VIEW"], []);
        AddPerms(perms, "SECURITY", "SIGNATURE_CONFIG", ["VIEW"], ["MANAGE"]);

        // COMPLIANCE module (2 — phase-04 Wave2 QĐ742 §4.4: data classification admin)
        AddPerms(perms, "COMPLIANCE", "CLASSIFICATION", ["VIEW"], ["MANAGE"]);

        // BUSINESSPROCESS module (3 — BP MVP P4: BP definition + run lifecycle admin; ACTIVATE separated for 4-eyes governance)
        AddPerms(perms, "BUSINESSPROCESS", "BP", ["VIEW"], ["MANAGE", "ACTIVATE"]);

        // SIGNATURE module (1 — dedicated least-privilege gate for the self-service enrollment approval queue)
        AddPerms(perms, "SIGNATURE", "ENROLLMENT", [], ["APPROVE"]);

        return perms;
    }

    /// <summary>
    /// Helper: creates Permission entities for a module/resource with normal + sensitive actions.
    /// Code format: MODULE.RESOURCE.ACTION. Name auto-generated from Vietnamese labels.
    /// </summary>
    private static void AddPerms(
        List<Permission> list,
        string module, string resource,
        string[] normalActions, string[] sensitiveActions)
    {
        foreach (var action in normalActions)
            list.Add(MakePerm(module, resource, action, isSensitive: false));
        foreach (var action in sensitiveActions)
            list.Add(MakePerm(module, resource, action, isSensitive: true));
    }

    private static Permission MakePerm(string module, string resource, string action, bool isSensitive)
    {
        var code = $"{module}.{resource}.{action}";
        return new Permission
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = GetPermissionName(module, resource, action),
            ModuleCode = module,
            ResourceCode = resource,
            ActionCode = action,
            IsSensitive = isSensitive
        };
    }

    /// <summary>Vietnamese permission name lookup by MODULE.RESOURCE.ACTION.</summary>
    private static string GetPermissionName(string module, string resource, string action)
    {
        var key = $"{module}.{resource}.{action}";
        return PermissionNames.GetValueOrDefault(key, key);
    }

    private static readonly Dictionary<string, string> PermissionNames = new()
    {
        // IDENTITY
        ["IDENTITY.USER.VIEW"] = "Xem người dùng",
        ["IDENTITY.USER.CREATE"] = "Tạo người dùng",
        ["IDENTITY.USER.UPDATE"] = "Cập nhật người dùng",
        ["IDENTITY.USER.DELETE"] = "Xóa người dùng",
        ["IDENTITY.ROLE.VIEW"] = "Xem vai trò",
        ["IDENTITY.ROLE.MANAGE"] = "Quản lý vai trò",
        ["IDENTITY.PERM.ASSIGN"] = "Gán quyền cho vai trò",
        ["IDENTITY.GROUP.VIEW"] = "Xem nhóm người dùng",
        ["IDENTITY.GROUP.MANAGE"] = "Quản lý nhóm",
        ["IDENTITY.DELEG.VIEW"] = "Xem ủy quyền",
        ["IDENTITY.DELEG.MANAGE"] = "Quản lý ủy quyền",
        ["IDENTITY.ABAC.VIEW"] = "Xem quy tắc ABAC",
        ["IDENTITY.ABAC.MANAGE"] = "Quản lý ABAC",
        ["IDENTITY.SOD.VIEW"] = "Xem quy tắc SoD",
        ["IDENTITY.SOD.MANAGE"] = "Quản lý SoD",
        ["IDENTITY.SOD.VIEW_AUDIT_LOG"] = "Xem nhật ký kiểm toán SoD",
        ["IDENTITY.SOD.EXPORT_AUDIT_LOG"] = "Xuất nhật ký kiểm toán SoD",
        ["IDENTITY.SESSION.VIEW"] = "Xem phiên đăng nhập",
        ["IDENTITY.SESSION.REVOKE"] = "Thu hồi phiên",
        ["IDENTITY.MENU.VIEW"] = "Xem menu",
        ["IDENTITY.MENU.MANAGE"] = "Quản lý menu",
        ["IDENTITY.POLICY.VIEW"] = "Xem chính sách bảo mật",
        ["IDENTITY.POLICY.MANAGE"] = "Quản lý chính sách",
        ["IDENTITY.CONSENT.VIEW"] = "Xem đồng ý PDPL",
        ["IDENTITY.DATASCOPE.MANAGE"] = "Quản lý phạm vi dữ liệu",
        ["IDENTITY.TENANT.VIEW"] = "Xem danh sách tenant",
        ["IDENTITY.TENANT.MANAGE"] = "Quản lý tenant",
        // FILES
        ["FILES.FILE.VIEW"] = "Xem tài liệu",
        ["FILES.FILE.UPLOAD"] = "Tải lên tài liệu",
        ["FILES.FILE.DELETE"] = "Xóa tài liệu",
        ["FILES.FILE.DOWNLOAD"] = "Tải xuống tài liệu",
        ["FILES.TEMPLATE.VIEW"] = "Xem mẫu tài liệu",
        ["FILES.TEMPLATE.MANAGE"] = "Quản lý mẫu tài liệu",
        ["FILES.RETENTION.VIEW"] = "Xem chính sách lưu trữ",
        ["FILES.RETENTION.MANAGE"] = "Quản lý chính sách lưu trữ",
        // AUDIT
        ["AUDIT.LOG.VIEW"] = "Xem nhật ký kiểm toán",
        ["AUDIT.LOG.EXPORT"] = "Xuất nhật ký",
        ["AUDIT.LOGIN.VIEW"] = "Xem lịch sử đăng nhập",
        ["AUDIT.RTBF.VIEW"] = "Xem yêu cầu RTBF",
        ["AUDIT.RTBF.EXECUTE"] = "Thực thi xóa dữ liệu RTBF",
        ["AUDIT.INCIDENT.VIEW"] = "Xem sự cố bảo mật",
        ["AUDIT.INCIDENT.MANAGE"] = "Quản lý sự cố bảo mật",
        // WORKFLOW
        ["WORKFLOW.DEF.VIEW"] = "Xem quy trình",
        ["WORKFLOW.DEF.MANAGE"] = "Quản lý quy trình",
        ["WORKFLOW.TASK.VIEW"] = "Xem công việc",
        ["WORKFLOW.TASK.PROCESS"] = "Xử lý công việc",
        ["WORKFLOW.INSTANCE.VIEW"] = "Xem tiến trình quy trình",
        ["WORKFLOW.INSTANCE.MANAGE"] = "Tạo & quản lý tiến trình quy trình",
        ["WORKFLOW.ASSIGN.MANAGE"] = "Phân công công việc",
        ["WORKFLOW.DELEGATION.MANAGE"] = "Quản lý uỷ quyền công việc",
        ["WORKFLOW.ANALYTICS.VIEW"] = "Xem phân tích quy trình",
        ["WORKFLOW.MIGRATION.READ"] = "Xem di chuyển phiên bản quy trình",
        ["WORKFLOW.MIGRATION.CREATE"] = "Tạo & xác thực di chuyển phiên bản",
        ["WORKFLOW.MIGRATION.APPROVE"] = "Phê duyệt di chuyển phiên bản",
        ["WORKFLOW.MIGRATION.EXECUTE"] = "Thực thi di chuyển phiên bản",
        ["WORKFLOW.MIGRATION.CANCEL"] = "Huỷ di chuyển phiên bản",
        // FORMS
        ["FORMS.TEMPLATE.VIEW"] = "Xem biểu mẫu",
        ["FORMS.TEMPLATE.MANAGE"] = "Quản lý biểu mẫu",
        ["FORMS.SUBMIT.VIEW"] = "Xem dữ liệu gửi",
        ["FORMS.SUBMIT.CREATE"] = "Gửi biểu mẫu",
        ["FORMS.SUBMIT.APPROVE"] = "Phê duyệt biểu mẫu",
        ["FORMS.SUBMIT.REJECT"] = "Từ chối biểu mẫu",
        ["FORMS.SUBMIT.EXPORT"] = "Xuất dữ liệu biểu mẫu",
        ["FORMS.VIEW.MANAGE"] = "Quản lý view biểu mẫu",
        ["FORMS.WFLINK.MANAGE"] = "Quản lý kết nối quy trình chờ xử lý (retry/shelve thủ công)",
        // NOTIFICATIONS
        ["NOTIF.MSG.VIEW"] = "Xem thông báo",
        ["NOTIF.MSG.SEND"] = "Gửi thông báo",
        ["NOTIF.TEMPLATE.VIEW"] = "Xem mẫu thông báo",
        ["NOTIF.TEMPLATE.MANAGE"] = "Quản lý mẫu thông báo",
        // ORGANIZATION
        ["ORG.UNIT.VIEW"] = "Xem đơn vị",
        ["ORG.UNIT.MANAGE"] = "Quản lý đơn vị",
        ["ORG.STAFF.VIEW"] = "Xem nhân sự",
        ["ORG.STAFF.MANAGE"] = "Quản lý phân công nhân sự",
        // MASTERDATA
        ["MASTER.DATA.VIEW"] = "Xem dữ liệu danh mục",
        ["MASTER.DATA.MANAGE"] = "Quản lý danh mục",
        ["MASTER.DICT.VIEW"] = "Xem từ điển dữ liệu",
        ["MASTER.DICT.MANAGE"] = "Quản lý từ điển",
        ["MASTER.EXTMAP.MANAGE"] = "Quản lý ánh xạ ngoài",
        // AI
        ["AI.EXTRACT.USE"] = "Sử dụng AI trích xuất",
        ["AI.MODEL.VIEW"] = "Xem cấu hình AI model",
        ["AI.MODEL.MANAGE"] = "Quản lý AI model",
        ["AI.PROMPT.VIEW"] = "Xem mẫu prompt",
        ["AI.PROMPT.MANAGE"] = "Quản lý mẫu prompt",
        // COLLABORATION
        ["COLLAB.CONV.VIEW"] = "Xem hội thoại",
        ["COLLAB.CONV.CREATE"] = "Tạo hội thoại",
        ["COLLAB.MSG.SEND"] = "Gửi tin nhắn",
        // INTEGRATION
        ["INTEG.CONTRACT.VIEW"] = "Xem hợp đồng tích hợp",
        ["INTEG.CONTRACT.MANAGE"] = "Quản lý hợp đồng",
        ["INTEG.PARTNER.VIEW"] = "Xem đối tác",
        ["INTEG.PARTNER.MANAGE"] = "Quản lý đối tác",
        ["INTEG.MSGLOG.VIEW"] = "Xem log tích hợp",
        // REPORTING
        ["REPORT.DASH.VIEW"] = "Xem dashboard",
        ["REPORT.DASH.MANAGE"] = "Quản lý dashboard",
        ["REPORT.REPORT.VIEW"] = "Xem báo cáo",
        ["REPORT.REPORT.MANAGE"] = "Quản lý báo cáo",
        ["REPORT.REPORT.EXPORT"] = "Xuất báo cáo",
        ["REPORT.WIDGET.MANAGE"] = "Quản lý widget",
        // RULES
        ["RULES.RULESET.VIEW"] = "Xem bộ quy tắc",
        ["RULES.RULESET.MANAGE"] = "Quản lý bộ quy tắc",
        ["RULES.DTABLE.VIEW"] = "Xem bảng quyết định",
        ["RULES.DTABLE.MANAGE"] = "Quản lý bảng quyết định",
        ["RULES.EVAL.USE"] = "Đánh giá quy tắc",
        // SEARCH
        ["SEARCH.QUERY.USE"] = "Tìm kiếm",
        ["SEARCH.SAVED.MANAGE"] = "Quản lý truy vấn đã lưu",
        // SYSTEMPARAMS
        ["SYSPARAM.PARAM.VIEW"] = "Xem tham số hệ thống",
        ["SYSPARAM.PARAM.MANAGE"] = "Quản lý tham số",
        ["SYSPARAM.FLAG.VIEW"] = "Xem feature flags",
        ["SYSPARAM.FLAG.MANAGE"] = "Quản lý feature flags",
        ["SYSPARAM.ANNOUNCE.VIEW"] = "Xem thông báo hệ thống",
        ["SYSPARAM.ANNOUNCE.MANAGE"] = "Quản lý thông báo",
        // SYSTEM
        ["SYSTEM.CONFIG.VIEW"] = "Xem cấu hình hệ thống",
        ["SYSTEM.CONFIG.MANAGE"] = "Quản lý cấu hình",
        // Phase-00 — Workflow taxonomy masterdata
        ["MASTER.WORKFLOW_TAXONOMY.VIEW"] = "Xem trạng thái/hành động của quy trình",
        ["PLATFORM.WORKFLOW_TAXONOMY.MANAGE"] = "Quản lý trạng thái/hành động của quy trình",
        // Phase-03 QĐ742 1.6.a — MFA policy + emergency disable
        ["SECURITY.MFA_POLICY.VIEW"] = "Xem chính sách MFA",
        ["SECURITY.MFA_POLICY.MANAGE"] = "Quản lý chính sách MFA",
        ["SECURITY.MFA_EMERGENCY.REQUEST"] = "Yêu cầu tắt MFA khẩn cấp",
        ["SECURITY.MFA_EMERGENCY.APPROVE"] = "Phê duyệt tắt MFA khẩn cấp",
        // Phase-05 QĐ742 §2.2.a — IP filter rule admin
        ["SECURITY.IP_RULE.VIEW"] = "Xem quy tắc lọc IP",
        ["SECURITY.IP_RULE.MANAGE"] = "Quản lý quy tắc lọc IP",
        // Phase-05 Wave-2 QĐ742 D7 — per-criterion compliance visibility
        ["SECURITY.HEALTH.VIEW"] = "Xem trạng thái bảo mật hệ thống",
        // Phase-05b W6 QĐ742 §5.1 — digital signature config admin
        ["SECURITY.SIGNATURE_CONFIG.VIEW"] = "Xem cấu hình chữ ký số",
        ["SECURITY.SIGNATURE_CONFIG.MANAGE"] = "Quản lý cấu hình chữ ký số",
        // Phase-04 Wave2 QĐ742 §4.4 — data classification admin
        ["COMPLIANCE.CLASSIFICATION.VIEW"] = "Xem kiểm kê phân loại dữ liệu",
        ["COMPLIANCE.CLASSIFICATION.MANAGE"] = "Quản lý quy tắc phân loại dữ liệu",
        // BP MVP P4 — BusinessProcess definition + run lifecycle admin
        ["BUSINESSPROCESS.BP.VIEW"] = "Xem quy trình nghiệp vụ",
        ["BUSINESSPROCESS.BP.MANAGE"] = "Quản lý quy trình nghiệp vụ",
        ["BUSINESSPROCESS.BP.ACTIVATE"] = "Kích hoạt/vô hiệu hóa quy trình nghiệp vụ",
        // SIGNATURE — self-service enrollment approval
        ["SIGNATURE.ENROLLMENT.APPROVE"] = "Duyệt đăng ký chứng thư chữ ký số",
    };
}
