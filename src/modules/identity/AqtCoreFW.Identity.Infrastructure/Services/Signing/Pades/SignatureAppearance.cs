namespace AqtCoreFW.Identity.Infrastructure.Services.Signing.Pades;

/// <summary>
/// A visible signature appearance: a pre-rendered <b>baseline JPEG</b> (the whole stamp — image + text — is baked
/// into the pixels by the rasterizer, so the writer embeds it as one image XObject and draws no text/fonts). The
/// rectangle is in <b>PDF user-space points with a bottom-left origin</b> (the standard PDF convention); the FE/
/// rasterizer owns any top-left→bottom-left flip. The writer validates the rect against the page MediaBox and
/// fails closed if it falls outside (red-team C2). Image <c>/Width</c>/<c>/Height</c>/<c>/ColorSpace</c> are read
/// from the JPEG itself, never supplied here (red-team H1/H2).
/// </summary>
/// <param name="JpegBytes">Baseline (SOF0) JPEG bytes of the rendered stamp.</param>
/// <param name="RectX">Lower-left X of the stamp rectangle, in PDF points (origin bottom-left).</param>
/// <param name="RectY">Lower-left Y of the stamp rectangle, in PDF points (origin bottom-left).</param>
/// <param name="RectWidth">Rectangle width in PDF points (> 0).</param>
/// <param name="RectHeight">Rectangle height in PDF points (> 0).</param>
/// <param name="Page">
/// 1-based page the stamp is drawn on. Defaults to 1. The writer resolves the Nth leaf page of a flat page tree
/// and validates the rect against THAT page's MediaBox; a non-flat tree or an out-of-range page fails closed.
/// </param>
public sealed record SignatureAppearance(
    byte[] JpegBytes,
    float RectX,
    float RectY,
    float RectWidth,
    float RectHeight,
    int Page = 1);
