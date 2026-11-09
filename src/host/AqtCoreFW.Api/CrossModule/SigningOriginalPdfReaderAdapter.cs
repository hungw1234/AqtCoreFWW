using AqtCoreFW.Files.Domain.Services;
using AqtCoreFW.Identity.Application.Signing;

namespace AqtCoreFW.Api.CrossModule;

/// <summary>
/// Host-resident adapter binding <see cref="ISigningOriginalPdfReader"/> (Identity.Application port) to
/// <see cref="IFileStorageService"/> (Files.Domain). Reads from the same <c>signing-original</c> bucket the
/// write-side <see cref="SigningOriginalBucketUploaderAdapter"/> stages into.
///
/// Lives in the API host — the only composition root permitted to reference both Identity and Files at compile
/// time, keeping a direct Identity.Infrastructure → Files.Domain dependency out of the module graph
/// (ModuleIsolationTests invariant). Registered in Program.cs after both module DI extensions.
/// </summary>
internal sealed class SigningOriginalPdfReaderAdapter(IFileStorageService fileStorage) : ISigningOriginalPdfReader
{
    // Must match the bucket constant in SigningOriginalBucketUploaderAdapter / StageFileForSigningCommandHandler.
    private const string SigningOriginalBucket = "signing-original";

    /// <inheritdoc />
    public async Task<byte[]> ReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        await using var stream = await fileStorage
            .DownloadAsync(SigningOriginalBucket, storageKey, cancellationToken)
            .ConfigureAwait(false);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        return ms.ToArray();
    }
}
