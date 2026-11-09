using FluentValidation;

namespace AqtCoreFW.Files.Application.Commands.StageFileForSigning;

public sealed class StageFileForSigningCommandValidator : AbstractValidator<StageFileForSigningCommand>
{
    public StageFileForSigningCommandValidator()
    {
        RuleFor(x => x.FileId).NotEmpty();
        RuleFor(x => x.TenantId).NotEmpty();
    }
}
