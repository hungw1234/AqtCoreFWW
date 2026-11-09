namespace AqtCoreFW.Identity.Infrastructure.Services.Signing.Pades;

/// <summary>
/// Reads the dimensions + component layout of a <b>baseline</b> JPEG straight from its SOF0 marker — the single
/// source of truth for the PDF image XObject's <c>/Width</c>, <c>/Height</c>, and <c>/ColorSpace</c> (so a
/// caller-supplied dimension can never disagree with the embedded bytes — red-team H1/H2). Fail-closed on
/// anything DCTDecode would render as garbage: a non-JPEG, a progressive JPEG (SOF2), an unexpected precision,
/// or an unsupported component count (red-team M1/L2).
/// </summary>
public sealed record JpegImageInfo(int Width, int Height, string ColorSpace)
{
    /// <summary>Parses the SOF0 (baseline DCT) frame header. Throws <see cref="PdfSigningException"/> on anything unsupported.</summary>
    public static JpegImageInfo Parse(byte[] jpeg)
    {
        ArgumentNullException.ThrowIfNull(jpeg);
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
            throw new PdfSigningException("Signature image is not a JPEG (missing SOI marker).");

        int i = 2;
        while (i + 1 < jpeg.Length)
        {
            // Markers begin with 0xFF; one or more 0xFF fill bytes may precede the marker code — skip them.
            if (jpeg[i] != 0xFF) throw new PdfSigningException("Malformed JPEG: expected a marker.");
            int j = i + 1;
            while (j < jpeg.Length && jpeg[j] == 0xFF) j++;
            if (j >= jpeg.Length) throw new PdfSigningException("Malformed JPEG: truncated marker.");
            byte marker = jpeg[j];
            i = j + 1;

            // Standalone markers (no length): SOI/EOI/RSTn/TEM — none carry a frame header.
            if (marker is 0xD8 or 0xD9 or 0x01 || marker is >= 0xD0 and <= 0xD7) continue;

            if (i + 1 >= jpeg.Length) throw new PdfSigningException("Malformed JPEG: truncated segment length.");
            int segLen = (jpeg[i] << 8) | jpeg[i + 1]; // big-endian, includes the 2 length bytes
            if (segLen < 2 || i + segLen > jpeg.Length) throw new PdfSigningException("Malformed JPEG: bad segment length.");

            switch (marker)
            {
                case 0xC0: // SOF0 — baseline sequential DCT (the only frame type DCTDecode renders reliably)
                    return ParseSof0(jpeg, i + 2, segLen - 2);
                case 0xC2: // SOF2 — progressive: many VN-PKI/Adobe RIPs choke on it via DCTDecode
                    throw new PdfSigningException("Progressive JPEG (SOF2) is not supported; render a baseline JPEG.");
                case 0xC1 or 0xC3 or (>= 0xC5 and <= 0xCF):
                    throw new PdfSigningException($"Unsupported JPEG frame type (SOF marker 0x{marker:X2}).");
                case 0xDA: // SOS — entropy-coded scan begins; we never reached an SOF0
                    throw new PdfSigningException("JPEG has no baseline SOF0 frame header before the scan.");
                default:
                    i += segLen; // skip APPn/DQT/DHT/COM/etc.
                    break;
            }
        }
        throw new PdfSigningException("JPEG ended before a frame header was found.");
    }

    private static JpegImageInfo ParseSof0(byte[] jpeg, int at, int len)
    {
        // SOF0 body: precision(1) height(2) width(2) numComponents(1) + 3*numComponents component specs.
        if (len < 6) throw new PdfSigningException("Malformed JPEG SOF0 header.");
        int precision = jpeg[at];
        if (precision != 8) throw new PdfSigningException($"Unsupported JPEG bit depth ({precision}); expected 8.");
        int height = (jpeg[at + 1] << 8) | jpeg[at + 2];
        int width = (jpeg[at + 3] << 8) | jpeg[at + 4];
        int components = jpeg[at + 5];
        if (height <= 0 || width <= 0) throw new PdfSigningException("JPEG reports a zero dimension.");

        string colorSpace = components switch
        {
            1 => "/DeviceGray",
            3 => "/DeviceRGB",
            4 => "/DeviceCMYK",
            _ => throw new PdfSigningException($"Unsupported JPEG component count ({components}).")
        };
        return new JpegImageInfo(width, height, colorSpace);
    }
}
