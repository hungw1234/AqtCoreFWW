using FluentValidation;

namespace AqtCoreFW.Audit.Application.Commands.CreateAiPromptTrace;

public sealed class CreateAiPromptTraceCommandValidator : AbstractValidator<CreateAiPromptTraceCommand>
{
    public CreateAiPromptTraceCommandValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.ModelName).NotEmpty().MaximumLength(256);
        RuleFor(x => x.PromptHash).NotEmpty().MaximumLength(128);
        RuleFor(x => x.InputTokens).GreaterThanOrEqualTo(0);
        RuleFor(x => x.OutputTokens).GreaterThanOrEqualTo(0);
        RuleFor(x => x.LatencyMs).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Cost).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ClassificationLevel).NotEmpty().MaximumLength(64);
        RuleFor(x => x.TenantId).NotEmpty();
    }
}
