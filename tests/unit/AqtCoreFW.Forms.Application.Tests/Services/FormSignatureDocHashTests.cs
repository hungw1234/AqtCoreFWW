using AqtCoreFW.Forms.Application.Services;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace AqtCoreFW.Forms.Application.Tests.Services;

/// <summary>
/// Determinism and correctness tests for <see cref="FormDataCanonicalizer.ComputeMode2DocHash"/>.
///
/// The Mode-2 signed artifact is:
///   SHA256( UTF8( JSON( {"template":code,"version":ver,"data":canonicalDataObject} ) ) )
/// where <c>canonicalDataObject</c> is the SAME ordinal-sorted scalar dict as Mode-1.
///
/// Determinism is the critical invariant: the FE fetches this hash at sign-time and the BE
/// recomputes it at ingest-time — any byte difference causes a false-reject on EVERY submission.
/// </summary>
public sealed class FormSignatureDocHashTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static FormField MakeActiveField(string key, FormFieldType type) =>
        FormField.Create(0, key, type, "VI", "EN", 1, false, 1);

    private static string ToHex(byte[] bytes) => Convert.ToHexString(bytes);

    private static byte[] ComputeHash(
        string code, int version,
        IDictionary<string, object?> data,
        IEnumerable<FormField> fields) =>
        FormDataCanonicalizer.ComputeMode2DocHash(code, version, data, fields);

    // ── Determinism ───────────────────────────────────────────────────────────────

    [Fact]
    public void ComputeMode2DocHash_SameInputTwice_ProducesIdenticalHash()
    {
        var fields = new List<FormField> { MakeActiveField("name", FormFieldType.Text) };
        var data   = new Dictionary<string, object?> { ["name"] = "Alice" };

        var hash1 = ComputeHash("my_form", 1, data, fields);
        var hash2 = ComputeHash("my_form", 1, data, fields);

        ToHex(hash1).Should().Be(ToHex(hash2),
            "identical inputs must produce identical hashes (determinism invariant)");
    }

    [Fact]
    public void ComputeMode2DocHash_Returns32Bytes()
    {
        var fields = new List<FormField> { MakeActiveField("name", FormFieldType.Text) };
        var data   = new Dictionary<string, object?> { ["name"] = "Alice" };

        var hash = ComputeHash("form", 1, data, fields);

        hash.Should().HaveCount(32, "SHA-256 always produces a 32-byte digest");
    }

    // ── Sensitivity ───────────────────────────────────────────────────────────────

    [Fact]
    public void ComputeMode2DocHash_DifferentFieldValue_ProducesDifferentHash()
    {
        var fields = new List<FormField> { MakeActiveField("name", FormFieldType.Text) };
        var data1  = new Dictionary<string, object?> { ["name"] = "Alice" };
        var data2  = new Dictionary<string, object?> { ["name"] = "Bob"   };

        var hash1 = ComputeHash("form", 1, data1, fields);
        var hash2 = ComputeHash("form", 1, data2, fields);

        ToHex(hash1).Should().NotBe(ToHex(hash2));
    }

    [Fact]
    public void ComputeMode2DocHash_DifferentTemplateVersion_ProducesDifferentHash()
    {
        // Changing the template version must change the hash — the wrapper binds template identity.
        var fields = new List<FormField> { MakeActiveField("name", FormFieldType.Text) };
        var data   = new Dictionary<string, object?> { ["name"] = "Alice" };

        var hash1 = ComputeHash("form", 1, data, fields);
        var hash2 = ComputeHash("form", 2, data, fields);

        ToHex(hash1).Should().NotBe(ToHex(hash2));
    }

    [Fact]
    public void ComputeMode2DocHash_DifferentTemplateCode_ProducesDifferentHash()
    {
        var fields = new List<FormField> { MakeActiveField("name", FormFieldType.Text) };
        var data   = new Dictionary<string, object?> { ["name"] = "Alice" };

        var hash1 = ComputeHash("form_a", 1, data, fields);
        var hash2 = ComputeHash("form_b", 1, data, fields);

        ToHex(hash1).Should().NotBe(ToHex(hash2));
    }

    // ── Input dict ordering does not matter ──────────────────────────────────────

    [Fact]
    public void ComputeMode2DocHash_InputDictOrderDoesNotAffectHash()
    {
        var fields = new List<FormField>
        {
            MakeActiveField("age",  FormFieldType.Number),
            MakeActiveField("name", FormFieldType.Text)
        };

        var dataAbc = new Dictionary<string, object?> { ["name"] = "Alice", ["age"] = "30" };
        var dataCba = new Dictionary<string, object?> { ["age"]  = "30",    ["name"] = "Alice" };

        var hash1 = ComputeHash("form", 1, dataAbc, fields);
        var hash2 = ComputeHash("form", 1, dataCba, fields);

        ToHex(hash1).Should().Be(ToHex(hash2),
            "ordinal key sorting makes the hash independent of input dict enumeration order");
    }

    // ── Same covered-field exclusion rule as Mode-1 ───────────────────────────────

    [Fact]
    public void ComputeMode2DocHash_FormulaAndUiOnlyFields_Excluded()
    {
        // Hash of {name=Alice, formula=999, section=header} must equal hash of {name=Alice only}
        var fieldsAll = new List<FormField>
        {
            MakeActiveField("name",    FormFieldType.Text),
            MakeActiveField("formula", FormFieldType.Formula),
            MakeActiveField("section", FormFieldType.Section)
        };
        var fieldsScalarOnly = new List<FormField>
        {
            MakeActiveField("name", FormFieldType.Text)
        };

        var dataAll    = new Dictionary<string, object?> { ["name"] = "Alice", ["formula"] = "999", ["section"] = "header" };
        var dataScalar = new Dictionary<string, object?> { ["name"] = "Alice" };

        var hash1 = ComputeHash("form", 1, dataAll,    fieldsAll);
        var hash2 = ComputeHash("form", 1, dataScalar, fieldsScalarOnly);

        ToHex(hash1).Should().Be(ToHex(hash2),
            "Formula/Section fields must be excluded from Mode-2 hash (same rule as Mode-1)");
    }

    [Fact]
    public void ComputeMode2DocHash_DigitalSignatureField_Excluded()
    {
        var fieldsWithDigSig = new List<FormField>
        {
            MakeActiveField("name",   FormFieldType.Text),
            MakeActiveField("digsig", FormFieldType.DigitalSignature)
        };
        var fieldsWithout = new List<FormField>
        {
            MakeActiveField("name", FormFieldType.Text)
        };

        var data = new Dictionary<string, object?> { ["name"] = "Alice", ["digsig"] = "ignored" };

        var hash1 = ComputeHash("form", 1, data, fieldsWithDigSig);
        var hash2 = ComputeHash("form", 1, new Dictionary<string, object?> { ["name"] = "Alice" }, fieldsWithout);

        ToHex(hash1).Should().Be(ToHex(hash2));
    }

    // ── NFC normalization (same rule as Mode-1) ──────────────────────────────────

    [Fact]
    public void ComputeMode2DocHash_NfdAndNfcInput_ProduceSameHash()
    {
        var nfc = "ă";   // precomposed U+0103
        var nfd = "ă";  // a + combining breve (decomposed)

        var fields = new List<FormField> { MakeActiveField("name", FormFieldType.Text) };

        var hash1 = ComputeHash("form", 1, new Dictionary<string, object?> { ["name"] = nfc }, fields);
        var hash2 = ComputeHash("form", 1, new Dictionary<string, object?> { ["name"] = nfd }, fields);

        ToHex(hash1).Should().Be(ToHex(hash2),
            "NFC normalization must make NFD and NFC inputs hash identically");
    }

    // ── Mode-1 / Mode-2 shared field selection (no drift) ────────────────────────

    [Fact]
    public void Mode1AndMode2_CoverExactlySameFields()
    {
        // If field selection diverged between Mode-1 and Mode-2, changing a non-covered field
        // would flip one hash but not the other → false-reject at ingest.
        // Verify that adding a Formula field changes neither mode's output.
        var fieldsWithFormula = new List<FormField>
        {
            MakeActiveField("name",  FormFieldType.Text),
            MakeActiveField("total", FormFieldType.Formula)
        };
        var fieldsScalar = new List<FormField>
        {
            MakeActiveField("name", FormFieldType.Text)
        };
        var dataWithFormula  = new Dictionary<string, object?> { ["name"] = "Alice", ["total"] = "999" };
        var dataScalar       = new Dictionary<string, object?> { ["name"] = "Alice" };

        // Mode-1: adding formula field does not change canonical bytes
        var m1WithFormula = FormDataCanonicalizer.CanonicalizeMode1(dataWithFormula, fieldsWithFormula);
        var m1Scalar      = FormDataCanonicalizer.CanonicalizeMode1(dataScalar,      fieldsScalar);
        ToHex(m1WithFormula).Should().Be(ToHex(m1Scalar), "Mode-1: formula excluded");

        // Mode-2: adding formula field does not change hash
        var m2WithFormula = ComputeHash("f", 1, dataWithFormula, fieldsWithFormula);
        var m2Scalar      = ComputeHash("f", 1, dataScalar,      fieldsScalar);
        ToHex(m2WithFormula).Should().Be(ToHex(m2Scalar), "Mode-2: formula excluded (same selection as Mode-1)");
    }

    // ── Pinned vector ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Pinned vector: template="survey", version=1, data={"name":"Alice"}.
    ///
    /// Wrapper JSON:  {"template":"survey","version":1,"data":{"name":"Alice"}}
    /// Wrapper UTF-8: computed and SHA-256 hashed below.
    ///
    /// This pin guards against accidental changes to the wrapper format, key ordering,
    /// or serialization options that would silently invalidate all Mode-2 signatures.
    /// </summary>
    [Fact]
    public void ComputeMode2DocHash_SingleTextField_MatchesPinnedVector()
    {
        var fields = new List<FormField> { MakeActiveField("name", FormFieldType.Text) };
        var data   = new Dictionary<string, object?> { ["name"] = "Alice" };

        var actual = ComputeHash("survey", 1, data, fields);

        // Compute expected offline:
        // wrapper JSON = {"template":"survey","version":1,"data":{"name":"Alice"}}
        // Using UnsafeRelaxedJsonEscaping + compact = same JSON
        var wrapperJson = "{\"template\":\"survey\",\"version\":1,\"data\":{\"name\":\"Alice\"}}";
        var wrapperBytes = Encoding.UTF8.GetBytes(wrapperJson);
        var expected = SHA256.HashData(wrapperBytes);

        ToHex(actual).Should().Be(ToHex(expected),
            "Mode-2 wrapper format must be: {\"template\":code,\"version\":ver,\"data\":canonicalDataObject}");
    }

    /// <summary>
    /// Pinned vector: template="form_v2", version=3, data={"age":"25","name":"Bob"} (ordinal-sorted).
    /// Verifies that version change and key ordering both affect the hash correctly.
    /// </summary>
    [Fact]
    public void ComputeMode2DocHash_TwoFieldsVersionThree_MatchesPinnedVector()
    {
        var fields = new List<FormField>
        {
            MakeActiveField("name", FormFieldType.Text),
            MakeActiveField("age",  FormFieldType.Number)
        };
        var data = new Dictionary<string, object?> { ["name"] = "Bob", ["age"] = "25" };

        var actual = ComputeHash("form_v2", 3, data, fields);

        // Ordinal sort: "age" < "name"
        var wrapperJson  = "{\"template\":\"form_v2\",\"version\":3,\"data\":{\"age\":\"25\",\"name\":\"Bob\"}}";
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(wrapperJson));

        ToHex(actual).Should().Be(ToHex(expected));
    }
}
