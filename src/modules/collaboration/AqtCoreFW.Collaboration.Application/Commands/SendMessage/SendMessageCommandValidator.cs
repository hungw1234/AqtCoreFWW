using FluentValidation;

namespace AqtCoreFW.Collaboration.Application.Commands.SendMessage;

public sealed class SendMessageCommandValidator : AbstractValidator<SendMessageCommand>
{
    public SendMessageCommandValidator()
    {
        RuleFor(x => x.ConversationId).NotEmpty();
        RuleFor(x => x.SenderId).NotEmpty();
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Content).NotEmpty().MaximumLength(10_000);
        RuleFor(x => x.ContentType).IsInEnum();
    }
}
