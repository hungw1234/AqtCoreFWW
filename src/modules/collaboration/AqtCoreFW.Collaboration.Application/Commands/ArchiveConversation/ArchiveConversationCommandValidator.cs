using FluentValidation;

namespace AqtCoreFW.Collaboration.Application.Commands.ArchiveConversation;

public sealed class ArchiveConversationCommandValidator : AbstractValidator<ArchiveConversationCommand>
{
    public ArchiveConversationCommandValidator()
    {
        RuleFor(x => x.ConversationId).NotEmpty();
        RuleFor(x => x.RequestedBy).NotEmpty();
    }
}
