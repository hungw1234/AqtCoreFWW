using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Services.Signing.Pades;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing.Strategies;

/// <summary>
/// Remote (Viettel Mysign) PAdES path: the provider is a hash-signer only, so the BE assembles the PAdES PDF
/// itself. Owns PDF artifacts for a server-side signer.
///
/// <para><b>Capability-exclusion (red-team C5).</b> Matches <see cref="SignatureProviderCapabilities.ServerSideSigning"/>
/// AND explicitly excludes <see cref="SignatureProviderCapabilities.ClientSideSigning"/>. Without the exclusion
/// this would also accept the USB token (which declares both <c>RawSign</c> and <c>ClientSideSigning</c>) and
/// then throw inside <c>SignAsync</c>; the exclusion keeps the matrix mutually exclusive with the doc-flow path.</para>
///
/// <para>Execution is async-poll: it delegates to <see cref="IPadesTransactionInitiator"/> which returns FAST with
/// a pollable transaction (no mobile-OTP block on the request thread); the CMS splice + final storage happen in the
/// completion path. Implementing <see cref="ISigningExecutionStrategy"/> here lifts the orchestrator's
/// <c>SIGNING_NO_EXECUTION_SURFACE</c> guard for the <c>(PDF × ServerSide|RawSign)</c> cell.</para>
/// </summary>
public sealed class PadesAssemblerSigningStrategy(IPadesTransactionInitiator initiator) : ISigningExecutionStrategy
{
    public string Name => "pades";

    public bool CanHandle(SigningArtifactKind artifactKind, SignatureProviderCapabilities resolvedCapabilities)
        => artifactKind is SigningArtifactKind.FormPdf or SigningArtifactKind.UploadedPdf
           && resolvedCapabilities.HasFlag(SignatureProviderCapabilities.ServerSideSigning)
           && !resolvedCapabilities.HasFlag(SignatureProviderCapabilities.ClientSideSigning);

    public Task<Result<SigningExecutionResult>> ExecuteAsync(
        SigningExecutionContext context, CancellationToken ct = default)
    {
        // Bad client input → 400, not 500: a PAdES route with no PAdES parameters cannot resolve the original PDF.
        if (context.Input.Pades is null)
            return Task.FromResult(Result.Fail<SigningExecutionResult>(new BadRequestError(
                "PAdES signing requires PAdES parameters (original storage key).", "SIGNING_PADES_PARAMS_REQUIRED")));

        return initiator.InitiateAsync(context, ct);
    }
}
