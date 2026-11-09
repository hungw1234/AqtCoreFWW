using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AqtCoreFW.Identity.Infrastructure.Services.Signing.Pades;
using AqtCoreFW.Identity.Infrastructure.Tests.Signing.Fixtures;
using FluentAssertions;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Cms;
using Org.BouncyCastle.X509;
using Xunit;
using Xunit.Abstractions;
using BcX509 = Org.BouncyCastle.X509;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Signing;

/// <summary>
/// The Phase-2 spike, automated: drive the FULL local-key pipeline (Prepare → signedAttrs → local RSA sign →
/// CMS → EmbedCms) and prove the produced PDF is STRUCTURALLY self-consistent and the embedded CMS verifies over
/// the ByteRange bytes — the automated half of the exit-gate. It also writes <c>signed.pdf</c> to disk so a human
/// can perform the MANUAL half (Adobe Reader + VGCA tool "green"), which no automated test can assert.
/// </summary>
public sealed class PadesSpikeEndToEndTests(ITestOutputHelper output)
{
    [Fact]
    public void LocalKeyPipeline_ProducesStructurallyValid_VerifiableSignedPdf()
    {
        // ── Arrange: sample PDF + the spike's local hash-signer ──────────────────────────────────
        byte[] inputPdf = MinimalPdfFactory.CreateSinglePage("AqtCoreFW — PAdES-B-B spike, local key");
        var writer = new PdfIncrementalSignatureWriter();
        var cmsBuilder = new PadesCmsBuilder();
        IRawHashSigner signer = new LocalRsaHashSigner();

        // ── Act: the exact two-call split Phase 3 will reuse (only `signer` swaps) ────────────────
        var prep = writer.Prepare(inputPdf, contentsHexLength: 32 * 1024); // sized for the real Mysign chain (F3)
        var attrs = cmsBuilder.BuildSignedAttributes(prep.DocumentDigest, signer.SignerChain[0], DateTime.UtcNow);
        byte[] rawSig = signer.Sign(attrs.SignInput); // ← Phase 3: mysign SignHashAsync(attrs.SignInput)
        byte[] cms = cmsBuilder.AssembleDetachedCms(attrs, rawSig, signer.SignerChain);
        byte[] signedPdf = writer.EmbedCms(prep, cms);

        // ── Assert: structural read-back over the SIGNED bytes (false-green guard) ─────────────────
        var (byteRange, contentsCms) = ReadSignature(signedPdf);
        byte[] coveredContent = ConcatRanges(signedPdf, byteRange);

        byte[] recomputed = SHA256.HashData(coveredContent);
        recomputed.Should().Equal(prep.DocumentDigest, because: "the signed PDF's ByteRange must reproduce messageDigest");

        var signedData = new CmsSignedData(new CmsProcessableByteArray(coveredContent), contentsCms);
        var certStore = signedData.GetCertificates();
        var signers = signedData.GetSignerInfos().GetSigners();
        signers.Should().HaveCount(1);
        foreach (SignerInformation si in signers)
        {
            BcX509.X509Certificate cert = certStore.EnumerateMatches(si.SignerID).First();
            si.Verify(cert).Should().BeTrue(
                because: "the CMS must verify over the document ByteRange — the automated half of the exit-gate");
        }

        // ── Side effect: emit signed.pdf for the MANUAL Adobe + VGCA gate ─────────────────────────
        string path = WriteArtifact(signedPdf);
        output.WriteLine($"Spike signed PDF written for manual Adobe/VGCA verification:\n  {path}");
        output.WriteLine($"ByteRange = [{string.Join(' ', byteRange)}]  size = {signedPdf.Length} bytes");
    }

    private static (int[] byteRange, byte[] cms) ReadSignature(byte[] pdf)
    {
        string text = Encoding.Latin1.GetString(pdf);
        var br = Regex.Match(text, @"/ByteRange \[\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s*\]");
        br.Success.Should().BeTrue();
        int[] range = [int.Parse(br.Groups[1].Value), int.Parse(br.Groups[2].Value),
                       int.Parse(br.Groups[3].Value), int.Parse(br.Groups[4].Value)];

        int open = text.IndexOf("/Contents <", StringComparison.Ordinal) + "/Contents <".Length;
        int close = text.IndexOf('>', open);
        byte[] gap = Convert.FromHexString(text[open..close]);
        // DER is self-delimiting: read the single CMS object and drop the trailing zero padding.
        using var ais = new Asn1InputStream(gap);
        byte[] cms = ais.ReadObject().GetEncoded("DER");
        return (range, cms);
    }

    private static byte[] ConcatRanges(byte[] pdf, int[] br)
    {
        byte[] result = new byte[br[1] + br[3]];
        Buffer.BlockCopy(pdf, br[0], result, 0, br[1]);
        Buffer.BlockCopy(pdf, br[2], result, br[1], br[3]);
        return result;
    }

    private static string WriteArtifact(byte[] signedPdf)
    {
        // Walk up to the repo root (the dir carrying both .git and plans/) so the artifact lands under the plan dir.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null &&
               !(Directory.Exists(Path.Combine(dir.FullName, ".git")) &&
                 Directory.Exists(Path.Combine(dir.FullName, "plans")))) dir = dir.Parent;
        string outDir = dir is null
            ? Path.Combine(Path.GetTempPath(), "pades-spike")
            : Path.Combine(dir.FullName, "plans", "260619-0835-signing-phase5-pades-embedded-spike", "reports", "artifacts");
        Directory.CreateDirectory(outDir);
        string path = Path.Combine(outDir, "spike-signed-local-key.pdf");
        File.WriteAllBytes(path, signedPdf);
        return path;
    }
}
