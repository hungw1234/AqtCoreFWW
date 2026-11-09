using FluentValidation;

namespace AqtCoreFW.Audit.Application.Commands.CreateAuditRetentionPolicy;

public sealed class CreateAuditRetentionPolicyCommandValidator : AbstractValidator<CreateAuditRetentionPolicyCommand>
{
    public CreateAuditRetentionPolicyCommandValidator()
    {
        RuleFor(x => x.RetentionDays)
            .GreaterThan(0)
            .LessThanOrEqualTo(365 * 10) // 10 years upper bound — guards against typo-as-decade
            .WithMessage("RetentionDays must be between 1 and 3650.");
        RuleFor(x => x.Category!.Value).IsInEnum().When(x => x.Category.HasValue);
        RuleFor(x => x.ArchivePath)
            .NotEmpty().MaximumLength(512)
            .When(x => x.ArchiveBeforePurge)
            .WithMessage("ArchivePath required when ArchiveBeforePurge is true.");
    }
}
