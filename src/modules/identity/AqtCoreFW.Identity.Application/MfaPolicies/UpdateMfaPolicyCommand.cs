using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using FluentValidation;
using MediatR;

namespace AqtCoreFW.Identity.Application.MfaPolicies;

/// <summary>Updates an existing MFA policy by public Guid (QĐ742 1.6.a).</summary>
public sealed record UpdateMfaPolicyCommand(
    Guid PublicId,
    bool IsMandatory,
    int GraceDays,
    MfaFactor AllowedFactors) : IRequest<Result>;

public sealed class UpdateMfaPolicyCommandValidator : AbstractValidator<UpdateMfaPolicyCommand>
{
    public UpdateMfaPolicyCommandValidator()
    {
        RuleFor(x => x.PublicId).NotEmpty();
        RuleFor(x => x.GraceDays).GreaterThanOrEqualTo(0)
            .WithMessage("GraceDays must be non-negative.");
        RuleFor(x => x.AllowedFactors)
            .Must(f => f != MfaFactor.None)
            .WithMessage("At least one MFA factor must be allowed.");
    }
}

public sealed class UpdateMfaPolicyCommandHandler(
    IMfaPolicyRepository repository,
    ICurrentUser currentUser)
    : IRequestHandler<UpdateMfaPolicyCommand, Result>
{
    public async Task<Result> Handle(UpdateMfaPolicyCommand request, CancellationToken ct)
    {
        var policy = await repository.GetByPublicIdAsync(request.PublicId, ct);
        if (policy is null)
            return Result.Fail(new NotFoundError($"MfaPolicy {request.PublicId} not found."));

        policy.Update(request.IsMandatory, request.GraceDays, request.AllowedFactors,
            currentUser.UserId);

        await repository.UpdateAsync(policy, ct);
        return Result.Ok();
    }
}
