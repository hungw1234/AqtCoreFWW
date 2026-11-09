using FluentValidation;

namespace AqtCoreFW.Files.Application.Commands.CreateRetentionPolicy;

public sealed class CreateRetentionPolicyCommandValidator : AbstractValidator<CreateRetentionPolicyCommand>
{
    public CreateRetentionPolicyCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.DocumentType).NotEmpty().MaximumLength(200);
        RuleFor(x => x.RetainDays).GreaterThan(0);
        RuleFor(x => x.ArchiveAfterDays).GreaterThan(0).When(x => x.ArchiveAfterDays.HasValue);
        RuleFor(x => x.DestroyAfterDays).GreaterThan(0).When(x => x.DestroyAfterDays.HasValue);
        RuleFor(x => x.DestroyAfterDays)
            .GreaterThanOrEqualTo(x => x.ArchiveAfterDays!.Value)
            .When(x => x.DestroyAfterDays.HasValue && x.ArchiveAfterDays.HasValue)
            .WithMessage("DestroyAfterDays must be >= ArchiveAfterDays.");
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.CreatedBy).NotEmpty();
    }
}
