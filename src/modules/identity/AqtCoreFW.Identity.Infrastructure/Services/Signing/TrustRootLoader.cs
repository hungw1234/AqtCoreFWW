using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing;

/// <summary>
/// Loads DER or PEM-encoded CA certificate files from the filesystem into raw byte arrays.
///
/// <para>
/// Each file is loaded independently: missing or unreadable files are warn-logged and skipped
/// (non-fatal). This prevents a misconfigured path from blocking startup entirely.
/// BouncyCastle's <c>X509CertificateParser.ReadCertificate</c> accepts both DER and PEM, so
/// raw bytes are returned as-is; the caller's parser handles the format.
/// </para>
/// </summary>
public static class TrustRootLoader
{
    /// <summary>
    /// Reads each path and returns the raw file bytes. Relative paths are resolved against
    /// <see cref="Directory.GetCurrentDirectory"/> (ContentRoot at startup in ASP.NET Core).
    /// Missing or unreadable files are warn-logged and skipped — never throws.
    /// </summary>
    /// <param name="paths">Absolute or relative filesystem paths to DER/PEM certificate files.</param>
    /// <param name="logger">Logger for warn-skip diagnostics; use <c>NullLogger.Instance</c> in tests.</param>
    /// <returns>Raw file bytes for each successfully loaded file, in input order.</returns>
    public static List<byte[]> LoadDer(IEnumerable<string> paths, ILogger logger)
    {
        var result = new List<byte[]>();

        foreach (var rawPath in paths)
        {
            // Resolve relative paths against CWD (= ContentRoot in ASP.NET Core hosted startup)
            var absolutePath = Path.IsPathRooted(rawPath)
                ? rawPath
                : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), rawPath));

            try
            {
                if (!File.Exists(absolutePath))
                {
                    logger.LogWarning(
                        "Trust store certificate file not found — skipping. Path: {AbsolutePath}",
                        absolutePath);
                    continue;
                }

                var bytes = File.ReadAllBytes(absolutePath);
                result.Add(bytes);

                logger.LogDebug(
                    "Loaded trust store certificate file ({Bytes} bytes). Path: {AbsolutePath}",
                    bytes.Length,
                    absolutePath);
            }
            catch (Exception ex)
            {
                // Non-fatal: warn and skip so one bad file doesn't block startup
                logger.LogWarning(
                    ex,
                    "Failed to read trust store certificate file — skipping. Path: {AbsolutePath}",
                    absolutePath);
            }
        }

        return result;
    }
}
