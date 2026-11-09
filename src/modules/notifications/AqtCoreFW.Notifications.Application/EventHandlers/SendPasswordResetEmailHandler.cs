using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Domain.Events;
using AqtCoreFW.Notifications.Application.Providers;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Notifications.Application.EventHandlers;

/// <summary>
/// Handles PasswordResetRequestedEvent from Identity module.
/// Sends password reset link via email (F-25: token never in API response).
/// </summary>
public sealed class SendPasswordResetEmailHandler(
    IEmailSender emailSender,
    ILogger<SendPasswordResetEmailHandler> logger) : INotificationHandler<PasswordResetRequestedEvent>
{
    public async Task Handle(PasswordResetRequestedEvent evt, CancellationToken ct)
    {
        var resetUrl = $"/reset-password?token={Uri.EscapeDataString(evt.ResetToken)}&userId={evt.UserId}";

        await emailSender.SendAsync(
            evt.Email,
            "Đặt lại mật khẩu — AqtCoreFW",
            $"Xin chào {evt.FullName},\n\n"
            + "Quản trị viên đã yêu cầu đặt lại mật khẩu cho tài khoản của bạn.\n"
            + $"Vui lòng truy cập liên kết sau để đặt mật khẩu mới:\n{resetUrl}\n\n"
            + "Liên kết có hiệu lực trong 24 giờ.\n"
            + "Nếu bạn không yêu cầu thay đổi này, vui lòng liên hệ quản trị viên.",
            ct);

        logger.LogInformation("Password reset email sent to {Email} for user {UserId}", PiiLogMasking.MaskEmail(evt.Email), evt.UserId);
    }
}
