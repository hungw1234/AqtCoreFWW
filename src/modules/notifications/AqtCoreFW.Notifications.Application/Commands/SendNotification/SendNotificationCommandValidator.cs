using FluentValidation;

namespace AqtCoreFW.Notifications.Application.Commands.SendNotification;

public sealed class SendNotificationCommandValidator : AbstractValidator<SendNotificationCommand>
{
    private static readonly string[] ValidChannels = ["email", "sms", "inapp"];

    public SendNotificationCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.RecipientUserId).NotEmpty();
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(20_000);
        RuleFor(x => x.Channel)
            .NotEmpty()
            .Must(c => ValidChannels.Contains(c.ToLowerInvariant()))
            .WithMessage("Channel must be one of: email, sms, inapp.");
        RuleFor(x => x.CorrelationId).MaximumLength(200).When(x => x.CorrelationId is not null);
    }
}
