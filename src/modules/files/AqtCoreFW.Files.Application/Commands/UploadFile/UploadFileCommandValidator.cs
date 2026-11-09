using FluentValidation;
using Microsoft.Extensions.Options;
using AqtCoreFW.Files.Application.Options;

namespace AqtCoreFW.Files.Application.Commands.UploadFile;

public sealed class UploadFileCommandValidator : AbstractValidator<UploadFileCommand>
{
    public UploadFileCommandValidator(IOptions<FilesOptions> options)
    {
        var maxBytes = options.Value.MaxFileSizeMb * 1024L * 1024L;

        RuleFor(x => x.OriginalFileName)
            .NotEmpty()
            .MaximumLength(255);

        RuleFor(x => x.SizeBytes)
            .GreaterThan(0)
            .LessThanOrEqualTo(maxBytes)
            .WithMessage($"File size must not exceed {options.Value.MaxFileSizeMb} MB.");

        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.UploadedBy).NotEmpty();
        RuleFor(x => x.FileStream).NotNull();
    }
}
