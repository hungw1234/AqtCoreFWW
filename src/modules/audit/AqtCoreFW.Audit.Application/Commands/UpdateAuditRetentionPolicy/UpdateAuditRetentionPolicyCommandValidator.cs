using FluentValidation;

namespace AqtCoreFW.Audit.Application.Commands.UpdateAuditRetentionPolicy;

public sealed class UpdateAuditRetentionPolicyCommandValidator : AbstractValidator<UpdateAuditRetentionPolicyCommand>
{
    public UpdateAuditRetentionPolicyCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.RetentionDays).GreaterThan(0).LessThanOrEqualTo(3650);
        RuleFor(x => x.RowVersion).NotEmpty();
        RuleFor(x => x.ArchivePath)
            .NotEmpty().MaximumLength(512)
            .When(x => x.ArchiveBeforePurge);
    }
}
