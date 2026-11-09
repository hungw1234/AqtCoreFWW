using FluentValidation;

namespace AqtCoreFW.Collaboration.Application.Commands.CreateConversation;

public sealed class CreateConversationCommandValidator : AbstractValidator<CreateConversationCommand>
{
    public CreateConversationCommandValidator()
    {
        RuleFor(x => x.CreatedBy).NotEmpty();
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.MemberUserIds)
            .NotNull()
            .Must(ids => ids.Count >= 1)
            .WithMessage("At least one member is required.");
        RuleFor(x => x.Name)
            .MaximumLength(200)
            .When(x => x.Name is not null);
    }
}
