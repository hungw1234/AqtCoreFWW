using AqtCoreFW.Audit.Domain.Enums;
using FluentValidation;

namespace AqtCoreFW.Audit.Application.Commands.ReviewAiOutput;

public sealed class ReviewAiOutputCommandValidator : AbstractValidator<ReviewAiOutputCommand>
{
    public ReviewAiOutputCommandValidator()
    {
        RuleFor(x => x.PromptTraceId).NotEmpty();
        RuleFor(x => x.ReviewerId).NotEmpty();
        RuleFor(x => x.Decision)
            .IsInEnum()
            .Must(d => d != ReviewDecision.Pending)
            .WithMessage("Decision must be Approved, Flagged, or Blocked — not Pending.");
        RuleFor(x => x.Reason).MaximumLength(2000).When(x => x.Reason is not null);
    }
}
