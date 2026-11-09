using FluentResults;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;

namespace AqtCoreFW.Identity.Application.SecurityHealth.Queries;

// ── Query ────────────────────────────────────────────────────────────────────

/// <summary>
/// QĐ 742 §4.4.a — D7 visibility: encryption key rotation status.
/// Reads <c>Encryption:CurrentVersion</c> from IConfiguration (same source as
/// <see cref="AqtCoreFW.Infrastructure.Configuration.VaultKeyResolver"/>).
///
/// Vault metadata (LastRotationAt, NextScheduledRotationAt) is NOT exposed by any
/// interface accessible from Identity.Application — returned as null until a
/// dedicated IKeyRotationMetadataReader is added to SharedKernel.Contracts (backlog).
/// </summary>
public sealed record GetKeyRotationStatusQuery : IRequest<Result<KeyRotationStatusDto>>;

public sealed class GetKeyRotationStatusQueryValidator
    : AbstractValidator<GetKeyRotationStatusQuery>
{
    public GetKeyRotationStatusQueryValidator() { /* no parameters */ }
}

// ── DTO ──────────────────────────────────────────────────────────────────────

/// <summary>
/// Encryption key rotation snapshot for the D7 compliance visibility page.
/// LastRotationAt and NextScheduledRotationAt are null until a metadata reader
/// interface is available cross-module (tracked backlog item).
/// </summary>
public sealed record KeyRotationStatusDto(
    /// <summary>
    /// Active encryption key version from Encryption:CurrentVersion config.
    /// Matches VaultKeyResolver.CurrentVersion logic (default 1).
    /// </summary>
    int CurrentKeyVersion,
    /// <summary>
    /// Null: Vault rotation metadata not exposed via any cross-module interface.
    /// Will be populated once IKeyRotationMetadataReader is added to SharedKernel.Contracts.
    /// </summary>
    DateTime? LastRotationAt,
    /// <summary>Null for the same reason as LastRotationAt.</summary>
    DateTime? NextScheduledRotationAt);

// ── Handler ──────────────────────────────────────────────────────────────────

public sealed class GetKeyRotationStatusQueryHandler(IConfiguration configuration)
    : IRequestHandler<GetKeyRotationStatusQuery, Result<KeyRotationStatusDto>>
{
    public Task<Result<KeyRotationStatusDto>> Handle(
        GetKeyRotationStatusQuery request, CancellationToken ct)
    {
        // Mirrors VaultKeyResolver: parse Encryption:CurrentVersion, default 1.
        var currentVersion = configuration.GetValue<int>("Encryption:CurrentVersion", 1);
        if (currentVersion < 1) currentVersion = 1;

        return Task.FromResult(Result.Ok(new KeyRotationStatusDto(
            CurrentKeyVersion:        currentVersion,
            LastRotationAt:           null,
            NextScheduledRotationAt:  null)));
    }
}
