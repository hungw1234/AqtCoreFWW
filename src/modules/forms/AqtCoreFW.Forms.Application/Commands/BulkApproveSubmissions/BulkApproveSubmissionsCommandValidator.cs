using FluentValidation;

namespace AqtCoreFW.Forms.Application.Commands.BulkApproveSubmissions;

public sealed class BulkApproveSubmissionsCommandValidator : AbstractValidator<BulkApproveSubmissionsCommand>
{
    public BulkApproveSubmissionsCommandValidator()
    {
        RuleFor(x => x.Ids).NotEmpty().WithMessage("At least one submission ID is required");
        RuleFor(x => x.Ids.Count).LessThanOrEqualTo(100)
            .WithMessage("Maximum 100 submissions per bulk operation");
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.ReviewedBy).NotEmpty();
    }
}
