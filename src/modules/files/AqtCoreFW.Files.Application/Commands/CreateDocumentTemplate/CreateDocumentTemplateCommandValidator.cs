using FluentValidation;

namespace AqtCoreFW.Files.Application.Commands.CreateDocumentTemplate;

public sealed class CreateDocumentTemplateCommandValidator : AbstractValidator<CreateDocumentTemplateCommand>
{
    public CreateDocumentTemplateCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description is not null);
        RuleFor(x => x.TemplateContent).NotEmpty();
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.CreatedBy).NotEmpty();
    }
}
