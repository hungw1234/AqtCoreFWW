using FluentValidation;

namespace AqtCoreFW.Integration.Application.Commands.CreatePartner;

public sealed class CreatePartnerCommandValidator : AbstractValidator<CreatePartnerCommand>
{
    public CreatePartnerCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.ContactEmail).MaximumLength(256).EmailAddress().When(x => x.ContactEmail != null);
        RuleFor(x => x.ContactPhone).MaximumLength(30);
        RuleFor(x => x.Endpoint).MaximumLength(500);
        RuleFor(x => x.AuthScheme).MaximumLength(100);
    }
}
