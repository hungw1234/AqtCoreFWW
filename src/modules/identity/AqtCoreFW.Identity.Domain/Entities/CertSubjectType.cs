namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>
/// Classification of a signing certificate subject: issued to a natural person (has a CCCD/CMND)
/// or to an organisation (has no national-id RDN). Derived at enrollment time from the cert subject
/// DN via <see cref="AqtCoreFW.Identity.Domain.Signing.VgcaCertSubjectParser.Classify"/>.
///
/// <para>Display-only — never used as a binding or access-control gate. Historical rows created
/// before this field was added default to <see cref="Personal"/> (0).</para>
/// </summary>
public enum CertSubjectType
{
    /// <summary>Natural-person certificate — subject DN contains a CCCD/CMND RDN.</summary>
    Personal = 0,

    /// <summary>Organisation certificate — subject DN has no national-id RDN; uses O or CN for display.</summary>
    Organization = 1,
}
