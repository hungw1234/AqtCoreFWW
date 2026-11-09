using FluentValidation;

namespace AqtCoreFW.Integration.Application.Commands.CreateMessageLog;

public sealed class CreateMessageLogCommandValidator : AbstractValidator<CreateMessageLogCommand>
{
    public CreateMessageLogCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.PartnerPublicId).NotEmpty();
        RuleFor(x => x.Direction).IsInEnum();
        RuleFor(x => x.MessageType).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CorrelationId).MaximumLength(200);
        // Fail-fast at API boundary: if caller provides a value it must be a real Guid (not Guid.Empty).
        // Use Must rather than NotEmpty — FluentValidation's NotEmpty on Guid? checks != null only,
        // not != Guid.Empty; Must(g => g != Guid.Empty) gives the explicit empty-Guid rejection.
        RuleFor(x => x.ContractPublicId)
            .Must(g => g != Guid.Empty)
            .When(x => x.ContractPublicId.HasValue)
            .WithMessage("'Contract Public Id' must not be empty.");
    }
}
