using FluentValidation;

namespace AqtCoreFW.Notifications.Application.Commands.CreateNotificationTemplate;

public sealed class CreateNotificationTemplateCommandValidator : AbstractValidator<CreateNotificationTemplateCommand>
{
    private static readonly string[] ValidChannels = ["email", "sms", "inapp"];

    public CreateNotificationTemplateCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.TemplateKey).NotEmpty().MaximumLength(100)
            .Matches("^[a-z0-9_]+$").WithMessage("TemplateKey must be lowercase alphanumeric with underscores.");
        RuleFor(x => x.SubjectTemplate).NotEmpty().MaximumLength(500);
        RuleFor(x => x.BodyTemplate).NotEmpty().MaximumLength(20_000);
        RuleFor(x => x.Channel)
            .NotEmpty()
            .Must(c => ValidChannels.Contains(c.ToLowerInvariant()))
            .WithMessage("Channel must be one of: email, sms, inapp.");
    }
}
