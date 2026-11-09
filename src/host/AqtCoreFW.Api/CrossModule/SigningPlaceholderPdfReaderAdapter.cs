using AqtCoreFW.Files.Domain.Services;
using AqtCoreFW.Identity.Application.Signing;

namespace AqtCoreFW.Api.CrossModule;

/// <summary>
/// Host-resident adapter binding <see cref="ISigningPlaceholderPdfReader"/> (Identity.Application port) to
/// <see cref="IFileStorageService"/> (Files.Domain). Reads from the same <c>signing-pades</c> bucket the write-side
/// <see cref="SigningPlaceholderPdfWriterAdapter"/> stages into — the completion path reads the placeholder back to
/// splice the polled CMS.
///
/// Lives in the API host — the only composition root permitted to reference both Identity and Files at compile
/// time (ModuleIsolationTests invariant). Registered in Program.cs after both module DI extensions.
/// </summary>
internal sealed class SigningPlaceholderPdfReaderAdapter(IFileStorageService fileStorage)
    : ISigningPlaceholderPdfReader
{
    // Must match the bucket constant in SigningPlaceholderPdfWriterAdapter.
    private const string SigningPadesBucket = "signing-pades";

    /// <inheritdoc />
    public async Task<byte[]> ReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        await using var stream = await fileStorage
            .DownloadAsync(SigningPadesBucket, storageKey, cancellationToken)
            .ConfigureAwait(false);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        return ms.ToArray();
    }
}
