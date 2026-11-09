namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>
/// Which visual signature image to embed in the visible PDF stamp.
/// Stored on the signing request appearance; not on the profile (user picks at sign-time).
/// </summary>
public enum SignatureVisualType
{
    /// <summary>Full handwritten-style signature image (longer strokes, full name).</summary>
    FullSignature = 1,

    /// <summary>Initial/paraph image (short strokes, abbreviated mark).</summary>
    InitialSignature = 2,
}
