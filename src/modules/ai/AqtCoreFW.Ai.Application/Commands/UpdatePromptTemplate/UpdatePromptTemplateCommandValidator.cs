using FluentValidation;

namespace AqtCoreFW.Ai.Application.Commands.UpdatePromptTemplate;

public sealed class UpdatePromptTemplateCommandValidator : AbstractValidator<UpdatePromptTemplateCommand>
{
    public UpdatePromptTemplateCommandValidator()
    {
        RuleFor(x => x.PublicId).NotEmpty();
        RuleFor(x => x.SystemPrompt).NotEmpty().MaximumLength(8000)
            .When(x => x.SystemPrompt is not null);
        RuleFor(x => x.UserPromptTemplate).NotEmpty().MaximumLength(4000)
            .When(x => x.UserPromptTemplate is not null);
    }
}
