using FluentValidation;

namespace AqtCoreFW.Ai.Application.Commands.CreateModelProfile;

public sealed class CreateModelProfileCommandValidator : AbstractValidator<CreateModelProfileCommand>
{
    public CreateModelProfileCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ModelId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Endpoint).MaximumLength(500).When(x => x.Endpoint is not null);
        RuleFor(x => x.MaxTokens).GreaterThan(0).LessThanOrEqualTo(1_000_000);
        RuleFor(x => x.Temperature).InclusiveBetween(0m, 2m);
        RuleFor(x => x.CostPerToken).GreaterThanOrEqualTo(0m);
    }
}
