using System.Threading;
using System.Threading.Tasks;
using AqtCoreFW.Forms.Application.Commands.SubmitForm;
using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Forms.Application.Tests.Commands;

/// <summary>
/// Tests server-side HTML sanitization of RichText field values in FormDataPreprocessor.
/// Mirrors FE DOMPurify allowlist — defense-in-depth against stored XSS.
/// </summary>
public sealed class FormDataPreprocessorRichTextTests
{
    private readonly IFormulaEvaluationService _formulaService = Substitute.For<IFormulaEvaluationService>();

    // FormField is T1 (long Id); templateId param is long
    private static FormField CreateRichTextField(string key = "notes") =>
        FormField.Create(
            1L, key, FormFieldType.RichText,
            "Ghi chú", "Notes", displayOrder: 1,
            required: false, addedInVersion: 1);

    private static FormField CreateTextField(string key = "name") =>
        FormField.Create(
            1L, key, FormFieldType.Text,
            "Tên", "Name", displayOrder: 2,
            required: false, addedInVersion: 1);

    [Fact]
    public async Task PreprocessData_RichText_PreservesAllowedTags()
    {
        var field = CreateRichTextField();
        var html = "<p><strong>Bold</strong> <em>italic</em> <u>underline</u></p>";
        var data = new Dictionary<string, object?> { ["notes"] = html };

        var result = await FormDataPreprocessor.PreprocessDataAsync([field], data, _formulaService, CancellationToken.None);

        result["notes"].Should().Be(html);
    }

    [Fact]
    public async Task PreprocessData_RichText_StripsScriptTags()
    {
        var field = CreateRichTextField();
        var data = new Dictionary<string, object?>
        {
            ["notes"] = "<p>Hello</p><script>alert('xss')</script>"
        };

        var result = await FormDataPreprocessor.PreprocessDataAsync([field], data, _formulaService, CancellationToken.None);

        result["notes"].As<string>().Should().NotContain("<script>");
        result["notes"].As<string>().Should().Contain("<p>Hello</p>");
    }

    [Fact]
    public async Task PreprocessData_RichText_StripsImgOnerror()
    {
        var field = CreateRichTextField();
        var data = new Dictionary<string, object?>
        {
            ["notes"] = "<p>Text</p><img src=x onerror=alert(1)>"
        };

        var result = await FormDataPreprocessor.PreprocessDataAsync([field], data, _formulaService, CancellationToken.None);

        result["notes"].As<string>().Should().NotContain("<img");
        result["notes"].As<string>().Should().NotContain("onerror");
    }

    [Fact]
    public async Task PreprocessData_RichText_StripsIframe()
    {
        var field = CreateRichTextField();
        var data = new Dictionary<string, object?>
        {
            ["notes"] = "<iframe src='https://evil.com'></iframe><p>Safe</p>"
        };

        var result = await FormDataPreprocessor.PreprocessDataAsync([field], data, _formulaService, CancellationToken.None);

        result["notes"].As<string>().Should().NotContain("<iframe");
        result["notes"].As<string>().Should().Contain("<p>Safe</p>");
    }

    [Fact]
    public async Task PreprocessData_RichText_StripsEventHandlers()
    {
        var field = CreateRichTextField();
        var data = new Dictionary<string, object?>
        {
            ["notes"] = "<p onclick=\"alert(1)\">Click me</p>"
        };

        var result = await FormDataPreprocessor.PreprocessDataAsync([field], data, _formulaService, CancellationToken.None);

        result["notes"].As<string>().Should().NotContain("onclick");
        result["notes"].As<string>().Should().Contain("<p>Click me</p>");
    }

    [Fact]
    public async Task PreprocessData_RichText_PreservesLinks()
    {
        var field = CreateRichTextField();
        var data = new Dictionary<string, object?>
        {
            ["notes"] = "<a href=\"https://gov.vn\" target=\"_blank\">Gov</a>"
        };

        var result = await FormDataPreprocessor.PreprocessDataAsync([field], data, _formulaService, CancellationToken.None);

        result["notes"].As<string>().Should().Contain("href=\"https://gov.vn\"");
    }

    [Fact]
    public async Task PreprocessData_RichText_NullValue_PassesThrough()
    {
        var field = CreateRichTextField();
        var data = new Dictionary<string, object?> { ["notes"] = null };

        var result = await FormDataPreprocessor.PreprocessDataAsync([field], data, _formulaService, CancellationToken.None);

        result["notes"].Should().BeNull();
    }

    [Fact]
    public async Task PreprocessData_RichText_EmptyString_PassesThrough()
    {
        var field = CreateRichTextField();
        var data = new Dictionary<string, object?> { ["notes"] = "" };

        var result = await FormDataPreprocessor.PreprocessDataAsync([field], data, _formulaService, CancellationToken.None);

        result["notes"].Should().Be("");
    }

    [Fact]
    public async Task PreprocessData_NonRichText_NoSanitization()
    {
        // Text fields should pass through as-is — no HTML stripping
        var field = CreateTextField();
        var data = new Dictionary<string, object?>
        {
            ["name"] = "<script>alert('xss')</script>"
        };

        var result = await FormDataPreprocessor.PreprocessDataAsync([field], data, _formulaService, CancellationToken.None);

        // Text fields are NOT sanitized — that's the correct behavior
        // (they're stored as plain text, not rendered as HTML)
        result["name"].Should().Be("<script>alert('xss')</script>");
    }

    [Fact]
    public async Task PreprocessData_RichText_StripsDangerousSchemes()
    {
        var field = CreateRichTextField();
        var data = new Dictionary<string, object?>
        {
            ["notes"] = "<a href=\"javascript:alert(1)\">Click</a>"
        };

        var result = await FormDataPreprocessor.PreprocessDataAsync([field], data, _formulaService, CancellationToken.None);

        result["notes"].As<string>().Should().NotContain("javascript:");
    }
}
