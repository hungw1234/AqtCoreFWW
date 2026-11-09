using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using FluentValidation;
using MediatR;

namespace AqtCoreFW.Identity.Application.MfaPolicies;

/// <summary>Creates a new per-role MFA policy (QĐ742 1.6.a).</summary>
public sealed record CreateMfaPolicyCommand(
    string Role,
    bool IsMandatory,
    int GraceDays,
    MfaFactor AllowedFactors) : IRequest<Result<Guid>>;

public sealed class CreateMfaPolicyCommandValidator : AbstractValidator<CreateMfaPolicyCommand>
{
    public CreateMfaPolicyCommandValidator()
    {
        RuleFor(x => x.Role).NotEmpty().MaximumLength(256);
        RuleFor(x => x.GraceDays).GreaterThanOrEqualTo(0)
            .WithMessage("GraceDays must be non-negative.");
        RuleFor(x => x.AllowedFactors)
            .Must(f => f != MfaFactor.None)
            .WithMessage("At least one MFA factor must be allowed.");
    }
}

public sealed class CreateMfaPolicyCommandHandler(
    IMfaPolicyRepository repository,
    ICurrentUser currentUser,
    IGuidGenerator guidGenerator)
    : IRequestHandler<CreateMfaPolicyCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CreateMfaPolicyCommand request, CancellationToken ct)
    {
        // Enforce unique-per-role constraint at application layer before DB insert.
        var existing = await repository.GetByRoleAsync(request.Role, ct);
        if (existing is not null)
            return Result.Fail(new ConflictError($"MFA policy for role '{request.Role}' already exists."));

        var policy = MfaPolicy.Create(
            guidGenerator,
            request.Role,
            request.IsMandatory,
            request.GraceDays,
            request.AllowedFactors,
            currentUser.UserId);

        await repository.AddAsync(policy, ct);
        // T2: return Guid PublicId (set by factory before SaveChanges)
        return Result.Ok(policy.PublicId);
    }
}
