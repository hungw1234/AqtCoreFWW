using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace AqtCoreFW.Api.Gateway;

/// <summary>
/// OpenAPI document transformer — adds Vietnamese metadata for Scalar UI.
/// Sets API title/description in Vietnamese and adds tag descriptions for all modules.
/// </summary>
public sealed class OpenApiVietnameseTransformer : IOpenApiDocumentTransformer
{
    // Tag → Vietnamese description mapping for all modules
    private static readonly Dictionary<string, string> TagDescriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Identity
        ["UsersAdmin"] = "Quản lý người dùng — tạo, cập nhật, khóa/mở, phân vai trò",
        ["RolesAdmin"] = "Danh mục vai trò và quyền hạn",
        ["GroupsAdmin"] = "Quản lý nhóm người dùng — thành viên, vai trò nhóm",
        ["Account"] = "Tài khoản cá nhân — đổi mật khẩu",
        ["Mfa"] = "Xác thực 2 yếu tố (MFA) — TOTP, OTP email",
        ["AbacRulesAdmin"] = "Quy tắc phân quyền ABAC (Attribute-Based Access Control)",
        ["AccessReview"] = "Rà soát quyền truy cập định kỳ",
        ["Consent"] = "Quản lý đồng ý PDPL (Luật 91/NĐ356)",
        ["CredentialPolicies"] = "Chính sách mật khẩu và xác thực",
        ["DataScopeAdmin"] = "Phạm vi dữ liệu — kiểm soát truy cập theo scope",
        ["Delegation"] = "Ủy quyền — chuyển giao quyền tạm thời",
        ["ExternalIdentities"] = "Tài khoản liên kết bên ngoài (VNeID, SSO)",
        ["PolicyRulesAdmin"] = "Quy tắc chính sách phân quyền",
        ["SessionAdmin"] = "Quản lý phiên đăng nhập hoạt động",
        ["SodRulesAdmin"] = "Quy tắc tách biệt nhiệm vụ (Segregation of Duties)",
        ["Menu"] = "Menu động theo vai trò người dùng",

        // Files
        ["Files"] = "Quản lý tệp tin — upload, download, quét virus (ClamAV)",
        ["DocumentTemplates"] = "Mẫu tài liệu — tạo, cập nhật, xuất bản phiên bản",
        ["FileVersions"] = "Lịch sử phiên bản tệp tin",
        ["RetentionPolicies"] = "Chính sách lưu giữ tệp tin (NĐ53)",

        // Forms
        ["FormTemplates"] = "Mẫu biểu mẫu động — trường dữ liệu, xuất bản, sao chép",
        ["FormSubmissions"] = "Nộp biểu mẫu — duyệt, từ chối, xuất CSV/PDF",
        ["FormSubmissionsExt"] = "Duyệt/từ chối hàng loạt + xuất PDF",
        ["PublicForms"] = "Biểu mẫu công khai — nộp ẩn danh (5 req/phút)",
        ["Views"] = "Cấu hình hiển thị danh sách (cột, bộ lọc, sắp xếp)",

        // Workflow
        ["WorkflowDefinitions"] = "Định nghĩa quy trình phê duyệt — trạng thái, chuyển đổi",
        ["WorkflowInstances"] = "Phiên bản quy trình — tạo, chuyển trạng thái, nhánh song song",
        ["WorkflowAssignments"] = "Quy tắc phân công tự động theo quy trình",

        // Audit
        ["AuditLogs"] = "Nhật ký kiểm toán — truy vấn, thống kê, xác minh HMAC (QĐ742)",
        ["SecurityIncidents"] = "Sự cố bảo mật — báo cáo, theo dõi, giải quyết",
        ["LoginAudit"] = "Lịch sử đăng nhập và phiên hoạt động",
        ["Rtbf"] = "Quyền được lãng quên — xóa dữ liệu cá nhân (PDPL)",

        // Notifications
        ["Notifications"] = "Thông báo — danh sách, đánh dấu đã đọc, gửi",
        ["NotificationTemplatesAdmin"] = "Mẫu thông báo (Liquid template) — email, SMS, in-app",

        // AI
        ["Ai"] = "Trí tuệ nhân tạo — chat, streaming, nạp tài liệu",
        ["AiExtractAgent"] = "OCR trích xuất tài liệu + ReAct agent + phân loại nội dung",
        ["AiModelProfiles"] = "Cấu hình AI model (provider, temperature, token limit)",
        ["AiPromptTemplates"] = "Mẫu prompt cho AI",

        // Reporting
        ["Reports"] = "Báo cáo — dashboard KPI, chạy báo cáo, xuất PDF tuân thủ",
        ["Dashboards"] = "Dashboard tùy chỉnh — bố cục, widget",
        ["Widgets"] = "Cấu hình widget cho dashboard",

        // Organization
        ["OrgUnits"] = "Cây tổ chức — đơn vị, thành viên, kế nhiệm",
        ["StaffAssignment"] = "Phân công nhân sự vào đơn vị",

        // MasterData
        ["MasterData"] = "Danh mục dùng chung — tỉnh/huyện/xã, loại hồ sơ, chức danh",
        ["MasterDataAdmin"] = "Quản trị danh mục — thêm, sửa, xóa",
        ["Dictionary"] = "Danh mục từ điển — giá trị enum, lookup",
        ["ExternalMapping"] = "Ánh xạ mã danh mục với hệ thống bên ngoài",

        // Collaboration
        ["Conversations"] = "Hội thoại — tạo, danh sách, lưu trữ",
        ["Messages"] = "Tin nhắn — gửi, xóa, tìm kiếm, đếm chưa đọc",

        // Integration
        ["Partners"] = "Đối tác tích hợp — quản lý thông tin",
        ["Contracts"] = "Hợp đồng tích hợp — tạo, cập nhật, vòng đời",
        ["MessageLogs"] = "Nhật ký trao đổi dữ liệu với hệ thống ngoài",

        // Search
        ["Search"] = "Tìm kiếm toàn văn — full-text, facet, cursor pagination",
        ["SavedQueries"] = "Truy vấn đã lưu — tạo, thực thi, cập nhật",

        // Rules
        ["RuleSets"] = "Bộ quy tắc — tạo, kích hoạt, phiên bản",
        ["RuleEvaluation"] = "Đánh giá quy tắc — thực thi, mô phỏng, giải thích",
        ["DecisionTables"] = "Bảng quyết định cho đánh giá quy tắc",

        // SystemParams
        ["FeatureFlags"] = "Cờ tính năng — bật/tắt tính năng hệ thống",
        ["SystemParams"] = "Tham số hệ thống — cấu hình toàn cục",
        ["Announcements"] = "Thông báo hệ thống — banner, bảo trì",

        // Admin (Host)
        ["ApiKeysAdmin"] = "API Key — tạo, thu hồi, xoay key (M2M)",
        ["AlertingAdmin"] = "Cấu hình cảnh báo hệ thống",
        ["BackupAdmin"] = "Quản lý sao lưu (NĐ53)",
        ["DeadLettersAdmin"] = "Hàng đợi tin nhắn lỗi — xem, retry",
        ["EventCatalogAdmin"] = "Danh mục schema sự kiện hệ thống",
        ["WebhooksAdmin"] = "Quản lý endpoint webhook bên ngoài",
    };

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken ct)
    {
        // Vietnamese API info
        document.Info = new OpenApiInfo
        {
            Title = "AqtCoreFW API v1",
            Description = """
                API backend cho các dự án Chính phủ số Việt Nam.
                Tuân thủ: Luật 91/2025/QH15 + NĐ356 (PDPL), NĐ53 (An ninh mạng), NĐ59 (VNeID), NĐ68 (Chữ ký số), QĐ742.

                **Xác thực:** Bearer JWT (OpenIddict) hoặc API Key (`X-Api-Key`).
                **Phân trang:** `page` (từ 1), `pageSize` (1–100). Trả về `meta.pagination`.
                **Rate Limiting:** Vượt giới hạn → HTTP 429 + `Retry-After` header.
                """,
            Version = "1.0.0",
            Contact = new OpenApiContact
            {
                Name = "AEQUITAS Engineering",
                Url = new Uri("https://aequitas.vn")
            }
        };

        // Apply Vietnamese tag descriptions
        document.Tags ??= new HashSet<OpenApiTag>();
        foreach (var tag in document.Tags)
        {
            if (tag.Name is not null && TagDescriptions.TryGetValue(tag.Name, out var desc))
                tag.Description = desc;
        }

        // Add missing tags that controllers registered but weren't in the tags list
        var existingTagNames = document.Tags.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, desc) in TagDescriptions)
        {
            if (!existingTagNames.Contains(name))
                document.Tags.Add(new OpenApiTag { Name = name, Description = desc });
        }

        return Task.CompletedTask;
    }
}
