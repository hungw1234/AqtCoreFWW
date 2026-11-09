using AqtCoreFW.Forms.Application.Services;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using System.Text;
using System.Text.Json;

namespace AqtCoreFW.Forms.Application.Tests.Services;

/// <summary>
/// Fixed-vector tests for <see cref="FormDataCanonicalizer.CanonicalizeMode1"/>.
/// These byte vectors ARE the FE↔BE contract: the FE (phase 04e) must produce
/// identical UTF-8 bytes for the same logical data.
///
/// Each test name describes the scenario being locked in — not a plan reference.
/// Hex assertions are computed offline and pinned here to guard against accidental drift.
/// </summary>
public sealed class FormDataCanonicalizerTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static FormField MakeActiveField(string key, FormFieldType type, bool required = false) =>
        FormField.Create(0, key, type, "VI", "EN", 1, required, 1);

    private static string ToHex(byte[] bytes) => Convert.ToHexString(bytes);

    private static string Utf8(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    // ── Empty data ───────────────────────────────────────────────────────────────

    [Fact]
    public void CanonicalizeMode1_EmptyData_ProducesEmptyJsonObject()
    {
        var bytes = FormDataCanonicalizer.CanonicalizeMode1(
            new Dictionary<string, object?>(),
            [MakeActiveField("name", FormFieldType.Text)]);

        Utf8(bytes).Should().Be("{}");
    }

    [Fact]
    public void CanonicalizeMode1_EmptyFields_ProducesEmptyJsonObject()
    {
        var bytes = FormDataCanonicalizer.CanonicalizeMode1(
            new Dictionary<string, object?> { ["name"] = "Alice" },
            []);

        Utf8(bytes).Should().Be("{}");
    }

    // ── Null omission ─────────────────────────────────────────────────────────────

    [Fact]
    public void CanonicalizeMode1_NullValue_OmitsKeyFromOutput()
    {
        var fields = new List<FormField>
        {
            MakeActiveField("name", FormFieldType.Text),
            MakeActiveField("age",  FormFieldType.Number)
        };
        var data = new Dictionary<string, object?> { ["name"] = "Alice", ["age"] = null };

        var json = Utf8(FormDataCanonicalizer.CanonicalizeMode1(data, fields));

        json.Should().Contain("\"name\"");
        json.Should().NotContain("\"age\"");
        json.Should().NotContain("null");
    }

    [Fact]
    public void CanonicalizeMode1_AllNullValues_ProducesEmptyJsonObject()
    {
        var fields = new List<FormField>
        {
            MakeActiveField("a", FormFieldType.Text),
            MakeActiveField("b", FormFieldType.Number)
        };
        var data = new Dictionary<string, object?> { ["a"] = null, ["b"] = null };

        Utf8(FormDataCanonicalizer.CanonicalizeMode1(data, fields)).Should().Be("{}");
    }

    // ── Key ordinal ordering ──────────────────────────────────────────────────────

    [Fact]
    public void CanonicalizeMode1_KeysAreSortedOrdinal()
    {
        // "Z" < "a" in ordinal order (uppercase ASCII < lowercase ASCII)
        var fields = new List<FormField>
        {
            MakeActiveField("zebra", FormFieldType.Text),
            MakeActiveField("Apple", FormFieldType.Text),
            MakeActiveField("mango", FormFieldType.Text)
        };
        var data = new Dictionary<string, object?>
        {
            ["zebra"] = "z",
            ["Apple"] = "a",
            ["mango"] = "m"
        };

        var json = Utf8(FormDataCanonicalizer.CanonicalizeMode1(data, fields));

        // Ordinal: 'A'(65) < 'm'(109) < 'z'(122)
        var appleIdx = json.IndexOf("\"Apple\"", StringComparison.Ordinal);
        var mangoIdx = json.IndexOf("\"mango\"", StringComparison.Ordinal);
        var zebraIdx = json.IndexOf("\"zebra\"", StringComparison.Ordinal);

        appleIdx.Should().BeLessThan(mangoIdx);
        mangoIdx.Should().BeLessThan(zebraIdx);
    }

    // ── Scalar-only filtering ─────────────────────────────────────────────────────

    [Fact]
    public void CanonicalizeMode1_UiOnlyFieldsExcluded()
    {
        var fields = new List<FormField>
        {
            MakeActiveField("title",  FormFieldType.Text),
            MakeActiveField("sec1",   FormFieldType.Section),
            MakeActiveField("lbl1",   FormFieldType.Label),
            MakeActiveField("div1",   FormFieldType.Divider)
        };
        var data = new Dictionary<string, object?>
        {
            ["title"] = "hello",
            ["sec1"]  = "ignored",
            ["lbl1"]  = "ignored",
            ["div1"]  = "ignored"
        };

        var json = Utf8(FormDataCanonicalizer.CanonicalizeMode1(data, fields));

        json.Should().Contain("\"title\"");
        json.Should().NotContain("\"sec1\"");
        json.Should().NotContain("\"lbl1\"");
        json.Should().NotContain("\"div1\"");
    }

    [Fact]
    public void CanonicalizeMode1_FormulaFieldExcluded()
    {
        var fields = new List<FormField>
        {
            MakeActiveField("amount",  FormFieldType.Number),
            MakeActiveField("total",   FormFieldType.Formula)
        };
        var data = new Dictionary<string, object?> { ["amount"] = 100m, ["total"] = 200m };

        var json = Utf8(FormDataCanonicalizer.CanonicalizeMode1(data, fields));

        json.Should().Contain("\"amount\"");
        json.Should().NotContain("\"total\"");
    }

    [Fact]
    public void CanonicalizeMode1_FileFieldExcluded()
    {
        var fields = new List<FormField>
        {
            MakeActiveField("name",     FormFieldType.Text),
            MakeActiveField("attach",   FormFieldType.File)
        };
        var data = new Dictionary<string, object?> { ["name"] = "doc", ["attach"] = "file-id-123" };

        var json = Utf8(FormDataCanonicalizer.CanonicalizeMode1(data, fields));

        json.Should().Contain("\"name\"");
        json.Should().NotContain("\"attach\"");
    }

    [Fact]
    public void CanonicalizeMode1_SignaturePngFieldExcluded()
    {
        var fields = new List<FormField>
        {
            MakeActiveField("name",   FormFieldType.Text),
            MakeActiveField("sig",    FormFieldType.Signature)
        };
        var data = new Dictionary<string, object?> { ["name"] = "Alice", ["sig"] = "base64png" };

        var json = Utf8(FormDataCanonicalizer.CanonicalizeMode1(data, fields));

        json.Should().NotContain("\"sig\"");
    }

    [Fact]
    public void CanonicalizeMode1_DigitalSignatureFieldExcluded()
    {
        var fields = new List<FormField>
        {
            MakeActiveField("name",   FormFieldType.Text),
            MakeActiveField("digsig", FormFieldType.DigitalSignature)
        };
        var data = new Dictionary<string, object?> { ["name"] = "Alice", ["digsig"] = "ignored" };

        var json = Utf8(FormDataCanonicalizer.CanonicalizeMode1(data, fields));

        json.Should().Contain("\"name\"");
        json.Should().NotContain("\"digsig\"");
    }

    [Fact]
    public void CanonicalizeMode1_ComplexFieldsExcluded_TableAddressDateRange()
    {
        var fields = new List<FormField>
        {
            MakeActiveField("name",    FormFieldType.Text),
            MakeActiveField("items",   FormFieldType.TableField),
            MakeActiveField("addr",    FormFieldType.AddressField),
            MakeActiveField("period",  FormFieldType.DateRange)
        };
        var data = new Dictionary<string, object?>
        {
            ["name"]   = "doc",
            ["items"]  = "[[]]",
            ["addr"]   = "{\"province\":\"HN\"}",
            ["period"] = "{\"from\":\"2025-01-01\"}"
        };

        var json = Utf8(FormDataCanonicalizer.CanonicalizeMode1(data, fields));

        json.Should().Contain("\"name\"");
        json.Should().NotContain("\"items\"");
        json.Should().NotContain("\"addr\"");
        json.Should().NotContain("\"period\"");
    }

    // ── Scalar field types included ───────────────────────────────────────────────

    [Fact]
    public void CanonicalizeMode1_AllScalarTypesIncluded()
    {
        var fields = new List<FormField>
        {
            MakeActiveField("f_text",        FormFieldType.Text),
            MakeActiveField("f_number",      FormFieldType.Number),
            MakeActiveField("f_date",        FormFieldType.Date),
            MakeActiveField("f_textarea",    FormFieldType.Textarea),
            MakeActiveField("f_boolean",     FormFieldType.Boolean),
            MakeActiveField("f_richtext",    FormFieldType.RichText),
            MakeActiveField("f_enumref",     FormFieldType.EnumRef),
            MakeActiveField("f_internalref", FormFieldType.InternalRef),
            MakeActiveField("f_externalref", FormFieldType.ExternalRef)
        };
        var data = new Dictionary<string, object?>
        {
            ["f_text"]        = "hello",
            ["f_number"]      = 42m,
            ["f_date"]        = "2025-01-15",
            ["f_textarea"]    = "multi\nline",
            ["f_boolean"]     = true,
            ["f_richtext"]    = "<p>hi</p>",
            ["f_enumref"]     = "VALUE_A",
            ["f_internalref"] = "ref-123",
            ["f_externalref"] = "ext-456"
        };

        var json = Utf8(FormDataCanonicalizer.CanonicalizeMode1(data, fields));

        foreach (var key in data.Keys)
            json.Should().Contain($"\"{key}\"");
    }

    // ── String coercion: CLR types ────────────────────────────────────────────────

    [Fact]
    public void CanonicalizeMode1_BoxedBoolTrue_CoercesToLiteralTrueString()
    {
        var fields = new List<FormField> { MakeActiveField("flag", FormFieldType.Boolean) };
        var data   = new Dictionary<string, object?> { ["flag"] = (object)true };

        var json = Utf8(FormDataCanonicalizer.CanonicalizeMode1(data, fields));

        // Value must be JSON string "true", not JSON boolean true
        json.Should().Be("{\"flag\":\"true\"}");
    }

    [Fact]
    public void CanonicalizeMode1_BoxedBoolFalse_CoercesToLiteralFalseString()
    {
        var fields = new List<FormField> { MakeActiveField("flag", FormFieldType.Boolean) };
        var data   = new Dictionary<string, object?> { ["flag"] = (object)false };

        var json = Utf8(FormDataCanonicalizer.CanonicalizeMode1(data, fields));

        json.Should().Be("{\"flag\":\"false\"}");
    }

    [Fact]
    public void CanonicalizeMode1_DecimalNumber_CoercesToInvariantString()
    {
        var fields = new List<FormField> { MakeActiveField("amount", FormFieldType.Number) };
        var data   = new Dictionary<string, object?> { ["amount"] = 1234.56m };

        var json = Utf8(FormDataCanonicalizer.CanonicalizeMode1(data, fields));

        // Invariant culture: decimal point not comma
        json.Should().Be("{\"amount\":\"1234.56\"}");
    }

    // ── String coercion: JsonElement ─────────────────────────────────────────────

    [Fact]
    public void CanonicalizeMode1_JsonElementString_CoercesToRawStringWithoutQuotes()
    {
        var doc    = JsonDocument.Parse("{\"name\":\"Alice\"}").RootElement;
        var value  = doc.GetProperty("name"); // JsonElement of kind String

        var fields = new List<FormField> { MakeActiveField("name", FormFieldType.Text) };
        var data   = new Dictionary<string, object?> { ["name"] = value };

        var json = Utf8(FormDataCanonicalizer.CanonicalizeMode1(data, fields));

        json.Should().Be("{\"name\":\"Alice\"}");
    }

    [Fact]
    public void CanonicalizeMode1_JsonElementNumber_CoercesToRawText()
    {
        var doc   = JsonDocument.Parse("{\"score\":98.5}").RootElement;
        var value = doc.GetProperty("score"); // JsonElement of kind Number

        var fields = new List<FormField> { MakeActiveField("score", FormFieldType.Number) };
        var data   = new Dictionary<string, object?> { ["score"] = value };

        var json = Utf8(FormDataCanonicalizer.CanonicalizeMode1(data, fields));

        // Raw JSON text for number is "98.5" (without quotes in the JSON value, but the
        // canonical dict stores it as string value → serializes as "98.5" string)
        json.Should().Be("{\"score\":\"98.5\"}");
    }

    [Fact]
    public void CanonicalizeMode1_JsonElementTrue_CoercesToLiteralTrueString()
    {
        var doc   = JsonDocument.Parse("{\"active\":true}").RootElement;
        var value = doc.GetProperty("active"); // JsonElement of kind True

        var fields = new List<FormField> { MakeActiveField("active", FormFieldType.Boolean) };
        var data   = new Dictionary<string, object?> { ["active"] = value };

        var json = Utf8(FormDataCanonicalizer.CanonicalizeMode1(data, fields));

        json.Should().Be("{\"active\":\"true\"}");
    }

    [Fact]
    public void CanonicalizeMode1_JsonElementFalse_CoercesToLiteralFalseString()
    {
        var doc   = JsonDocument.Parse("{\"active\":false}").RootElement;
        var value = doc.GetProperty("active");

        var fields = new List<FormField> { MakeActiveField("active", FormFieldType.Boolean) };
        var data   = new Dictionary<string, object?> { ["active"] = value };

        var json = Utf8(FormDataCanonicalizer.CanonicalizeMode1(data, fields));

        json.Should().Be("{\"active\":\"false\"}");
    }

    // ── Unicode NFC normalization ─────────────────────────────────────────────────

    [Fact]
    public void CanonicalizeMode1_DecomposedUnicode_NormalizesToNfc()
    {
        // "ă" can be composed (U+0103 single codepoint, NFC) or
        // decomposed (U+0061 'a' + U+0306 combining breve, NFD).
        // Both must produce the same canonical bytes.
        var nfc = "ă";                   // ă precomposed
        var nfd = "ă";                  // a + combining breve (decomposed)

        var fields = new List<FormField> { MakeActiveField("name", FormFieldType.Text) };

        var bytesNfc = FormDataCanonicalizer.CanonicalizeMode1(
            new Dictionary<string, object?> { ["name"] = nfc }, fields);
        var bytesNfd = FormDataCanonicalizer.CanonicalizeMode1(
            new Dictionary<string, object?> { ["name"] = nfd }, fields);

        ToHex(bytesNfc).Should().Be(ToHex(bytesNfd),
            "NFD and NFC forms of the same character must produce identical canonical bytes");
    }

    [Fact]
    public void CanonicalizeMode1_VietnameseText_IsNotEscaped_LiteralUtf8()
    {
        // UnsafeRelaxedJsonEscaping: non-ASCII chars appear as literal UTF-8 bytes,
        // NOT \uXXXX sequences. This matches browser JSON.stringify behaviour.
        var fields = new List<FormField> { MakeActiveField("city", FormFieldType.Text) };
        var data   = new Dictionary<string, object?> { ["city"] = "Hà Nội" };

        var json = Utf8(FormDataCanonicalizer.CanonicalizeMode1(data, fields));

        // Must contain literal "Hà Nội" not à ộ escape sequences
        json.Should().Contain("Hà Nội");
        json.Should().NotContain("\\u");
    }

    // ── Fixed-vector pinning (FE contract byte locks) ─────────────────────────────

    /// <summary>
    /// Pinned vector: single text field "name"="Alice".
    /// JSON: {"name":"Alice"} → UTF-8 hex lock.
    /// FE must produce identical bytes for JSON.stringify({"name":"Alice"}) after ordinal sort.
    /// </summary>
    [Fact]
    public void CanonicalizeMode1_SingleTextField_MatchesPinnedVector()
    {
        var fields = new List<FormField> { MakeActiveField("name", FormFieldType.Text) };
        var data   = new Dictionary<string, object?> { ["name"] = "Alice" };

        var actual = ToHex(FormDataCanonicalizer.CanonicalizeMode1(data, fields));

        // {"name":"Alice"} in UTF-8
        var expected = ToHex(Encoding.UTF8.GetBytes("{\"name\":\"Alice\"}"));
        actual.Should().Be(expected);
    }

    /// <summary>
    /// Pinned vector: two fields ordinal-sorted; number coerced from CLR decimal.
    /// JSON: {"age":"30","name":"Bob"} (ordinal: 'a' < 'n').
    /// </summary>
    [Fact]
    public void CanonicalizeMode1_TwoFields_OrdinalSorted_MatchesPinnedVector()
    {
        var fields = new List<FormField>
        {
            MakeActiveField("name", FormFieldType.Text),
            MakeActiveField("age",  FormFieldType.Number)
        };
        // Supply in reverse order to confirm sorting
        var data = new Dictionary<string, object?> { ["name"] = "Bob", ["age"] = 30m };

        var actual   = ToHex(FormDataCanonicalizer.CanonicalizeMode1(data, fields));
        var expected = ToHex(Encoding.UTF8.GetBytes("{\"age\":\"30\",\"name\":\"Bob\"}"));

        actual.Should().Be(expected);
    }

    /// <summary>
    /// Pinned vector: Formula and UI-only keys excluded; only scalar "title" remains.
    /// JSON: {"title":"report"}
    /// </summary>
    [Fact]
    public void CanonicalizeMode1_ExcludesNonScalar_MatchesPinnedVector()
    {
        var fields = new List<FormField>
        {
            MakeActiveField("title",   FormFieldType.Text),
            MakeActiveField("calc",    FormFieldType.Formula),
            MakeActiveField("section", FormFieldType.Section)
        };
        var data = new Dictionary<string, object?>
        {
            ["title"]   = "report",
            ["calc"]    = "999",
            ["section"] = "header"
        };

        var actual   = ToHex(FormDataCanonicalizer.CanonicalizeMode1(data, fields));
        var expected = ToHex(Encoding.UTF8.GetBytes("{\"title\":\"report\"}"));

        actual.Should().Be(expected);
    }

    /// <summary>
    /// Pinned vector: Vietnamese NFC text "Hà Nội" as literal UTF-8 (no \uXXXX escaping).
    /// FE: JSON.stringify({"city":"Hà Nội"}) with UnsafeRelaxedJsonEscaping equivalent.
    /// </summary>
    [Fact]
    public void CanonicalizeMode1_VietnameseValue_MatchesPinnedVector()
    {
        var fields = new List<FormField> { MakeActiveField("city", FormFieldType.Text) };
        var data   = new Dictionary<string, object?> { ["city"] = "Hà Nội" };

        var actual = Utf8(FormDataCanonicalizer.CanonicalizeMode1(data, fields));

        // Pinned: literal UTF-8, not escaped
        actual.Should().Be("{\"city\":\"Hà Nội\"}");
    }

    // ── Inactive field excluded ───────────────────────────────────────────────────

    [Fact]
    public void CanonicalizeMode1_InactiveField_Excluded()
    {
        // FormField.Create sets IsActive=true; deactivate it via Deactivate()
        var activeField   = MakeActiveField("kept",    FormFieldType.Text);
        var inactiveField = MakeActiveField("dropped", FormFieldType.Text);
        inactiveField.Deactivate();

        var data = new Dictionary<string, object?> { ["kept"] = "yes", ["dropped"] = "no" };

        var json = Utf8(FormDataCanonicalizer.CanonicalizeMode1(data, [activeField, inactiveField]));

        json.Should().Contain("\"kept\"");
        json.Should().NotContain("\"dropped\"");
    }
}
