using FluentValidation;

namespace AqtCoreFW.Ai.Application.Commands.CreatePromptTemplate;

public sealed class CreatePromptTemplateCommandValidator : AbstractValidator<CreatePromptTemplateCommand>
{
    public CreatePromptTemplateCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.SystemPrompt).NotEmpty().MaximumLength(8000);
        RuleFor(x => x.UserPromptTemplate).NotEmpty().MaximumLength(4000);
    }
}
