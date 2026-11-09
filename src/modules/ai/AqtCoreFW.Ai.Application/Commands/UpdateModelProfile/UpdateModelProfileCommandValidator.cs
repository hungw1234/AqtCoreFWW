using FluentValidation;

namespace AqtCoreFW.Ai.Application.Commands.UpdateModelProfile;

public sealed class UpdateModelProfileCommandValidator : AbstractValidator<UpdateModelProfileCommand>
{
    public UpdateModelProfileCommandValidator()
    {
        RuleFor(x => x.PublicId).NotEmpty();
        RuleFor(x => x.MaxTokens).GreaterThan(0).LessThanOrEqualTo(1_000_000)
            .When(x => x.MaxTokens.HasValue);
        RuleFor(x => x.Temperature).InclusiveBetween(0m, 2m)
            .When(x => x.Temperature.HasValue);
    }
}
