using System.Security.Cryptography;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using FluentResults;
using FluentValidation;
using MediatR;

namespace AqtCoreFW.Identity.Application.Signing.Profiles;

/// <summary>Challenge returned to the client to sign for possession-proof enrollment.</summary>
public sealed record EnrollmentChallengeDto(Guid NonceId, string ChallengeBase64, DateTime ExpiresAt);

/// <summary>
/// Phase 1 of self-service enrollment: issues a single-use, user-bound possession-proof challenge. The
/// caller's identity is JWT-injected (never body-bound). The client signs the returned challenge — via
/// Mysign phone-OTP (Remote) or the VGCA USB plugin (Usb) — then submits the signature to
/// <see cref="EnrollMySignatureCertCommand"/>.
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record IssueEnrollmentNonceCommand(
    Guid TenantId,
    Guid CallerUserId) : ICommand<EnrollmentChallengeDto>;

public sealed class IssueEnrollmentNonceCommandValidator : AbstractValidator<IssueEnrollmentNonceCommand>
{
    public IssueEnrollmentNonceCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.CallerUserId).NotEmpty();
    }
}

public sealed class IssueEnrollmentNonceCommandHandler(
    ISignerEnrollmentNonceRepository nonces,
    IGuidGenerator guidGen)
    : IRequestHandler<IssueEnrollmentNonceCommand, Result<EnrollmentChallengeDto>>
{
    // 5-minute window: long enough for the phone-OTP / USB-PIN round-trip, short enough to bound replay.
    private const int TtlSeconds = 300;

    public async Task<Result<EnrollmentChallengeDto>> Handle(IssueEnrollmentNonceCommand cmd, CancellationToken ct)
    {
        var challenge = RandomNumberGenerator.GetBytes(32);
        var nonce = SignerEnrollmentNonce.Issue(guidGen, cmd.TenantId, cmd.CallerUserId, challenge, TtlSeconds);
        await nonces.AddAsync(nonce, ct);

        return Result.Ok(new EnrollmentChallengeDto(
            nonce.Id, Convert.ToBase64String(challenge), nonce.ExpiresAt));
    }
}
