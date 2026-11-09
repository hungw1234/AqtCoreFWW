using FluentValidation;

namespace AqtCoreFW.Notifications.Application.Commands.CreateNotification;

public sealed class CreateNotificationCommandValidator : AbstractValidator<CreateNotificationCommand>
{
    private static readonly string[] ValidChannels = ["email", "sms", "inapp"];

    public CreateNotificationCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.RecipientUserId).NotEmpty();
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(10_000);
        RuleFor(x => x.Channel)
            .NotEmpty()
            .Must(c => ValidChannels.Contains(c.ToLowerInvariant()))
            .WithMessage("Channel must be one of: email, sms, inapp.");
    }
}
