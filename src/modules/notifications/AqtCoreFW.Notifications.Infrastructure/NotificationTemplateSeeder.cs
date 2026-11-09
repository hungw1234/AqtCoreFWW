using AqtCoreFW.Notifications.Domain.Entities;
using AqtCoreFW.Notifications.Domain.ValueObjects;
using AqtCoreFW.Notifications.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Notifications.Infrastructure;

/// <summary>
/// Seeds default notification templates at startup (idempotent).
/// Templates use Scriban syntax for dynamic content rendering.
/// Global templates (TenantId = Guid.Empty) serve as fallback defaults.
/// </summary>
public sealed class NotificationTemplateSeeder(
    IServiceScopeFactory scopeFactory,
    AqtCoreFW.Infrastructure.Security.AmbientTenantScope ambientTenant,
    ILogger<NotificationTemplateSeeder> logger) : IHostedService, IIntegrationTestSeeder
{
    private static readonly Guid SystemUserId = Guid.Empty;
    private static readonly Guid GlobalTenantId = Guid.Empty;

    public async Task StartAsync(CancellationToken ct)
    {
        // Push tenant-zero as the ambient tenant BEFORE creating the DI scope: under a
        // non-db_owner login the RLS FILTER on notifications.NotificationTemplates hides
        // tenant-zero rows from a scope with no SESSION_CONTEXT tenant, so the existence
        // check below goes blind and the re-insert dies on the unique index (constraint
        // enforcement runs engine-level regardless of the FILTER predicate) — CrashLoop on
        // the first NonAudit-activated boot, 2026-07-23.
        // The ambient push routes ITenantContext to ScopedTenantContext(Guid.Empty) →
        // TenantSessionContextInterceptor pins SESSION_CONTEXT → FILTER matches the
        // global rows for both the read and the BLOCK-AFTER-INSERT predicate.
        using var tenantZero = ambientTenant.Push(GlobalTenantId);
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var guidGen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var seeded = 0;
        foreach (var (key, subject, body, channel) in DefaultTemplates)
        {
            var exists = await db.NotificationTemplates.AnyAsync(
                t => t.TemplateKey == key && t.Channel == channel && t.TenantId == GlobalTenantId, ct);

            if (!exists)
            {
                db.NotificationTemplates.Add(NotificationTemplate.Create(
                    guidGen, GlobalTenantId, key, subject, body, channel, SystemUserId, isDefault: true));
                seeded++;
            }
        }

        if (seeded > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("NotificationTemplateSeeder: seeded {Count} templates", seeded);
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    // ─── Default Templates ────────────────────────────────────────────────────

    private static readonly (string Key, string Subject, string Body, NotificationChannel Channel)[] DefaultTemplates =
    [
        // ── Case lifecycle (Email) ──────────────────────────────────────
        ("case_created", "Hồ sơ {{ case_number }} đã được tiếp nhận",
            WrapHtml("""
            <h2>Xin chào {{ recipient_name }},</h2>
            <p>Hồ sơ <strong>{{ case_number }}</strong> của bạn đã được tiếp nhận thành công.</p>
            <table style="width:100%;border-collapse:collapse;margin:16px 0;">
              <tr><td style="padding:8px;border:1px solid #ddd;font-weight:bold;">Mã hồ sơ</td><td style="padding:8px;border:1px solid #ddd;">{{ case_number }}</td></tr>
              <tr><td style="padding:8px;border:1px solid #ddd;font-weight:bold;">Mã tra cứu</td><td style="padding:8px;border:1px solid #ddd;">{{ tracking_code }}</td></tr>
              <tr><td style="padding:8px;border:1px solid #ddd;font-weight:bold;">Trạng thái</td><td style="padding:8px;border:1px solid #ddd;">Đã tiếp nhận</td></tr>
            </table>
            <p>Bạn có thể tra cứu tiến độ xử lý tại cổng dịch vụ công.</p>
            """), NotificationChannel.Email),

        ("case_approved", "Hồ sơ {{ case_number }} đã được phê duyệt",
            WrapHtml("""
            <h2>Xin chào {{ recipient_name }},</h2>
            <p>Hồ sơ <strong>{{ case_number }}</strong> đã được <span style="color:#16a34a;font-weight:bold;">phê duyệt</span>.</p>
            {{ if reason }}<p><em>Ghi chú: {{ reason }}</em></p>{{ end }}
            <p>Vui lòng đến nhận kết quả theo hướng dẫn tại cổng dịch vụ công.</p>
            """), NotificationChannel.Email),

        ("case_rejected", "Hồ sơ {{ case_number }} cần bổ sung",
            WrapHtml("""
            <h2>Xin chào {{ recipient_name }},</h2>
            <p>Hồ sơ <strong>{{ case_number }}</strong> cần <span style="color:#dc2626;font-weight:bold;">bổ sung thông tin</span>.</p>
            {{ if reason }}<p><strong>Lý do:</strong> {{ reason }}</p>{{ end }}
            <p>Vui lòng đăng nhập và cập nhật hồ sơ trong vòng 10 ngày làm việc.</p>
            """), NotificationChannel.Email),

        // ── Identity (Email) ────────────────────────────────────────────
        ("password_reset", "Yêu cầu đặt lại mật khẩu",
            WrapHtml("""
            <h2>Xin chào {{ recipient_name }},</h2>
            <p>Chúng tôi nhận được yêu cầu đặt lại mật khẩu cho tài khoản của bạn.</p>
            <div style="text-align:center;margin:24px 0;">
              <a href="{{ reset_url }}" style="background:#2563eb;color:#fff;padding:12px 32px;text-decoration:none;border-radius:6px;font-weight:bold;">Đặt lại mật khẩu</a>
            </div>
            <p style="color:#6b7280;font-size:13px;">Liên kết này có hiệu lực trong 30 phút. Nếu bạn không yêu cầu, vui lòng bỏ qua email này.</p>
            """), NotificationChannel.Email),

        ("account_locked", "Tài khoản bị khóa",
            WrapHtml("""
            <h2>Xin chào {{ recipient_name }},</h2>
            <p>Tài khoản của bạn đã bị <span style="color:#dc2626;font-weight:bold;">khóa</span> do đăng nhập sai quá số lần cho phép.</p>
            <p>Vui lòng liên hệ quản trị viên hoặc sử dụng chức năng đặt lại mật khẩu.</p>
            """), NotificationChannel.Email),

        // ── SLA Warning (Email) ─────────────────────────────────────────
        ("sla_breached", "⚠ SLA vi phạm — Hồ sơ {{ case_number }}",
            WrapHtml("""
            <h2>Cảnh báo SLA</h2>
            <p>Hồ sơ <strong>{{ case_number }}</strong> đã vượt quá thời hạn xử lý quy định.</p>
            <table style="width:100%;border-collapse:collapse;margin:16px 0;">
              <tr><td style="padding:8px;border:1px solid #ddd;font-weight:bold;">Thời gian đã qua</td><td style="padding:8px;border:1px solid #ddd;">{{ elapsed_hours }} giờ</td></tr>
              <tr><td style="padding:8px;border:1px solid #ddd;font-weight:bold;">Trạng thái hiện tại</td><td style="padding:8px;border:1px solid #ddd;">{{ current_state }}</td></tr>
            </table>
            <p>Vui lòng xử lý ngay hoặc liên hệ lãnh đạo phụ trách.</p>
            """), NotificationChannel.Email),

        // ── In-App notifications ────────────────────────────────────────
        ("case_created", "Hồ sơ {{ case_number }} đã được tiếp nhận",
            "Hồ sơ **{{ case_number }}** của bạn đã được tiếp nhận. Mã tra cứu: {{ tracking_code }}",
            NotificationChannel.InApp),

        ("case_approved", "Hồ sơ {{ case_number }} đã được phê duyệt",
            "Hồ sơ **{{ case_number }}** đã được phê duyệt.{{ if reason }} Ghi chú: {{ reason }}{{ end }}",
            NotificationChannel.InApp),

        ("case_rejected", "Hồ sơ {{ case_number }} cần bổ sung",
            "Hồ sơ **{{ case_number }}** cần bổ sung.{{ if reason }} Lý do: {{ reason }}{{ end }}",
            NotificationChannel.InApp),

        // ── SMS notifications ───────────────────────────────────────────
        ("case_created", "Hồ sơ tiếp nhận",
            "Ho so {{ case_number }} da duoc tiep nhan. Ma tra cuu: {{ tracking_code }}. Chi tiet tai cong dich vu cong.",
            NotificationChannel.Sms),

        ("password_reset", "Đặt lại mật khẩu",
            "Ma OTP dat lai mat khau: {{ otp_code }}. Hieu luc 5 phut. Khong chia se ma nay.",
            NotificationChannel.Sms),
    ];

    /// <summary>Wraps body content in a responsive GOV-branded HTML email layout.</summary>
    private static string WrapHtml(string innerContent) => $"""
        <!DOCTYPE html>
        <html lang="vi">
        <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1.0"></head>
        <body style="margin:0;padding:0;background:#f3f4f6;font-family:'Segoe UI',Roboto,Arial,sans-serif;">
          <table width="100%" cellpadding="0" cellspacing="0" style="background:#f3f4f6;padding:24px 0;">
            <tr><td align="center">
              <table width="600" cellpadding="0" cellspacing="0" style="background:#fff;border-radius:8px;overflow:hidden;box-shadow:0 1px 3px rgba(0,0,0,0.1);">
                <tr><td style="background:#1e40af;padding:20px 32px;">
                  <h1 style="margin:0;color:#fff;font-size:18px;">Cổng Dịch vụ công</h1>
                </td></tr>
                <tr><td style="padding:32px;">
                  {innerContent}
                </td></tr>
                <tr><td style="background:#f9fafb;padding:16px 32px;border-top:1px solid #e5e7eb;">
                  <p style="margin:0;color:#9ca3af;font-size:12px;text-align:center;">
                    Email tự động — vui lòng không trả lời. © 2026 AEQUITAS
                  </p>
                </td></tr>
              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;
}
