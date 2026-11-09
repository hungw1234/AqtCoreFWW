using AqtCoreFW.Files.Domain.Services;
using AqtCoreFW.Identity.Application.Signing;

namespace AqtCoreFW.Api.CrossModule;

/// <summary>
/// Host-resident adapter binding <see cref="ISigningPlaceholderPdfWriter"/> (Identity.Application port) to
/// <see cref="IFileStorageService"/> (Files.Domain). Stages the PAdES placeholder PDF into the <c>signing-pades</c>
/// bucket — the same bucket the completion path (P4-4) reads the placeholder back from and writes the final signed
/// PDF into (distinct key suffixes).
///
/// Lives in the API host — the only composition root permitted to reference both Identity and Files at compile
/// time, keeping a direct Identity.Infrastructure → Files.Domain dependency out of the module graph
/// (ModuleIsolationTests invariant). Registered in Program.cs after both module DI extensions.
/// </summary>
internal sealed class SigningPlaceholderPdfWriterAdapter(IFileStorageService fileStorage)
    : ISigningPlaceholderPdfWriter
{
    // Staging bucket for server-side PAdES artifacts (placeholder awaiting CMS, then the spliced signed PDF).
    private const string SigningPadesBucket = "signing-pades";

    /// <inheritdoc />
    public async Task<string> WriteAsync(byte[] bytes, string key, CancellationToken cancellationToken = default)
    {
        // Stream-based API; the placeholder is a single PDF (a few hundred KB), so a MemoryStream is safe.
        using var stream = new MemoryStream(bytes, writable: false);
        return await fileStorage.UploadAsync(
            stream, SigningPadesBucket, key, "application/pdf", cancellationToken);
    }
}
