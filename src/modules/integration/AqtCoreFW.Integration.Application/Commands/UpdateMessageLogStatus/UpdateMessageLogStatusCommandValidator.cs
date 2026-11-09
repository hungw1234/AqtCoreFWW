using FluentValidation;

namespace AqtCoreFW.Integration.Application.Commands.UpdateMessageLogStatus;

public sealed class UpdateMessageLogStatusCommandValidator : AbstractValidator<UpdateMessageLogStatusCommand>
{
    public UpdateMessageLogStatusCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.NewStatus).IsInEnum();
    }
}
