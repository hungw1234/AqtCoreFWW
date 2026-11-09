namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>
/// Renders a <see cref="ComposedStamp"/> (the user's signature image + the composed Vietnamese text lines)
/// into a single <b>baseline JPEG</b> raster sized to the target rectangle — the one image the PAdES writer
/// embeds as a DCTDecode image XObject in the signature widget's appearance stream (Approach R: raster the
/// whole stamp, alpha flattened onto an opaque background since JPEG has no transparency).
///
/// <para>The implementation lives in Infrastructure (it depends on the raster engine). The output MUST be a
/// baseline (SOF0) JPEG so the writer's <c>JpegImageInfo</c> parser accepts it — a progressive JPEG is
/// rejected downstream, fail-closed.</para>
/// </summary>
public interface ISignatureStampRasterizer
{
    /// <summary>
    /// Rasterizes <paramref name="stamp"/> to a baseline-JPEG byte array fitting a
    /// <paramref name="widthPoints"/> × <paramref name="heightPoints"/> rectangle (PDF points). The pixel
    /// dimensions are chosen for print legibility; the writer scales the image 1:1 onto the widget rect.
    /// </summary>
    byte[] Rasterize(ComposedStamp stamp, float widthPoints, float heightPoints);
}
