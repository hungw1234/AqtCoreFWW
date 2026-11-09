using FluentValidation;

namespace AqtCoreFW.Forms.Application.Commands.BulkRejectSubmissions;

public sealed class BulkRejectSubmissionsCommandValidator : AbstractValidator<BulkRejectSubmissionsCommand>
{
    public BulkRejectSubmissionsCommandValidator()
    {
        RuleFor(x => x.Ids).NotEmpty().WithMessage("At least one submission ID is required");
        RuleFor(x => x.Ids.Count).LessThanOrEqualTo(100)
            .WithMessage("Maximum 100 submissions per bulk operation");
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.ReviewedBy).NotEmpty();
        RuleFor(x => x.Comment).NotEmpty().WithMessage("Rejection comment is required for audit trail");
    }
}
