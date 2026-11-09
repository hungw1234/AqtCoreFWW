using FluentValidation;

namespace AqtCoreFW.Files.Application.Commands.UploadFileVersion;

public sealed class UploadFileVersionCommandValidator : AbstractValidator<UploadFileVersionCommand>
{
    public UploadFileVersionCommandValidator()
    {
        RuleFor(x => x.FileRecordId).NotEmpty();
        RuleFor(x => x.StoragePath).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.FileSize).GreaterThan(0);
        RuleFor(x => x.ContentHash).NotEmpty().MaximumLength(256);
        RuleFor(x => x.UploadedBy).NotEmpty();
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Comment).MaximumLength(1000).When(x => x.Comment is not null);
    }
}
