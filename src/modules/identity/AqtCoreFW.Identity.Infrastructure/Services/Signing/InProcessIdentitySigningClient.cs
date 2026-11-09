using System.Text.Json;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.SharedKernel.Contracts.Clients;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing;

/// <summary>
/// In-process implementation of <see cref="IIdentitySigningClient"/> — a thin façade over the
/// existing verify pipeline (<see cref="ISignatureProviderResolver"/> + provider
/// <see cref="IDigitalSignatureProvider.VerifyAsync"/>). No new crypto; it resolves the tenant's
/// OCSP mode + provider, maps the contract DTO to the Domain <see cref="VerifyRequest"/>, and maps
/// the Domain <see cref="VerifyResult"/> back to audit-grade evidence on the contract DTO.
///
/// A missing provider or unknown format is mapped to IsValid=false (not thrown) so the Forms
/// verify-at-ingest can hard-block uniformly on any verify failure — a misconfiguration is a
/// verification failure to the caller, never a 500.
/// </summary>
public sealed class InProcessIdentitySigningClient(
    ISignatureProviderResolver resolver,
    ITenantSignatureConfigRepository configRepository,
    ISignerBindingGuard signerBindingGuard) : IIdentitySigningClient
{
    public async Task<SignatureVerifyResult> VerifySignatureAsync(
        SignatureVerifyRequest request, CancellationToken ct = default)
    {
        var verifiedAt = DateTimeOffset.UtcNow;

        // OCSP mode from tenant config; fail-safe Soft when no enabled config (dev runbook H3).
        var config = await configRepository.GetByTenantIdAsync(request.TenantId, ct);
        var ocspMode = config is { IsEnabled: true } ? config.OcspMode : OcspMode.Soft;

        // Prefer the caller-supplied provider key (same one that drove signing); fall back to config.
        var providerKey = string.IsNullOrWhiteSpace(request.ProviderKey)
            ? config?.ProviderKey
            : request.ProviderKey;
        if (string.IsNullOrWhiteSpace(providerKey))
        {
            return Failure("provider-not-configured", verifiedAt);
        }

        if (!TryParseFormat(request.Format, out var format))
        {
            return Failure($"unknown-format:{request.Format}", verifiedAt);
        }

        IDigitalSignatureProvider provider;
        try
        {
            provider = resolver.Resolve(providerKey);
        }
        catch (KeyNotFoundException)
        {
            // No provider registered for the configured key — surface as a verify failure, not a 500.
            // Boundary assumes the non-capability Resolve(key) overload (returns null → KeyNotFound).
            // A genuine provider crash (e.g. remote transport failure) is intentionally NOT caught here:
            // it is a system fault that must propagate, not be laundered into a false "invalid" verdict.
            return Failure($"provider-not-registered:{providerKey}", verifiedAt);
        }

        var domainRequest = new VerifyRequest(
            Payload: request.Payload,
            Signature: request.Signature,
            OcspMode: ocspMode,
            SignerCertDer: request.SignerCertDer,
            HashAlg: request.HashAlg,
            Encode: request.Encode,
            Format: format);

        var result = await provider.VerifyAsync(domainRequest, ct);

        // Anti-impersonation: a cryptographically-valid signature is only accepted when the signing
        // certificate provably belongs to the assigned signer. Skipped when no signer is supplied
        // (e.g. anonymous public-form submission has no assigned signer to bind to).
        if (result.IsValid
            && request.SignerUserId is Guid signerUserId
            && result.SigningCertificate is not null)
        {
            var verdict = await signerBindingGuard.VerifyAsync(
                result.SigningCertificate, signerUserId, request.TenantId, ct);
            if (!verdict.IsBound)
            {
                return new SignatureVerifyResult
                {
                    IsValid       = false,
                    FailureReason = $"signer-binding-failed: {verdict.Reason}",
                    SignerSubject = result.SigningCertificate.Subject,
                    SignerSerial  = result.SigningCertificate.SerialNumber,
                    SignerCertDer = result.SigningCertificate.RawData,
                    // Keep the validated-chain evidence: the cert verified cryptographically but did
                    // not match the assigned signer — exactly the case forensics needs the chain for.
                    ChainEvidenceJson = result.ChainResult is null ? null : SerializeChainEvidence(result.ChainResult),
                    VerifiedAt    = verifiedAt,
                };
            }
        }

        return new SignatureVerifyResult
        {
            IsValid = result.IsValid,
            FailureReason = result.FailureReason,
            SignerSubject = result.SigningCertificate?.Subject,
            SignerSerial = result.SigningCertificate?.SerialNumber,
            SignerCertDer = result.SigningCertificate?.RawData,
            ChainEvidenceJson = result.ChainResult is null ? null : SerializeChainEvidence(result.ChainResult),
            VerifiedAt = verifiedAt,
        };
    }

    private static SignatureVerifyResult Failure(string reason, DateTimeOffset verifiedAt) =>
        new() { IsValid = false, FailureReason = reason, VerifiedAt = verifiedAt };

    private static bool TryParseFormat(string? value, out SignatureFormat format)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "raw": format = SignatureFormat.Raw; return true;
            case "pkcs7": format = SignatureFormat.Pkcs7; return true;
            case "xmldsig": format = SignatureFormat.XmlDsig; return true;
            case "jws": format = SignatureFormat.Jws; return true;
            default: format = default; return false;
        }
    }

    private static string SerializeChainEvidence(ChainValidationResult chain)
    {
        var dto = new ChainEvidenceDto
        {
            IsValid = chain.IsValid,
            FailureReason = chain.FailureReason,
            OcspChecked = chain.OcspChecked,
            OcspSucceeded = chain.OcspSucceeded,
            Chain = chain.Chain.Select(c => new ChainCertEvidenceDto
            {
                SerialNumber = c.SerialNumber,
                Subject = c.Subject,
                Issuer = c.Issuer,
                NotBefore = c.NotBefore,
                NotAfter = c.NotAfter,
                Thumbprint = c.Thumbprint,
                RawDataBase64 = Convert.ToBase64String(c.RawData),
            }).ToList(),
        };
        return JsonSerializer.Serialize(dto);
    }
}
