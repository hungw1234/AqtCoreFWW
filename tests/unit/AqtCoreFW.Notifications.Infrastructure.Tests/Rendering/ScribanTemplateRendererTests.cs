using AqtCoreFW.Notifications.Infrastructure.Rendering;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AqtCoreFW.Notifications.Infrastructure.Tests.Rendering;

/// <summary>
/// TC-NOT-I001: ScribanTemplateRenderer renders variables correctly.
/// TC-NOT-I002: ScribanTemplateRenderer handles missing variables gracefully.
/// TC-NOT-I003: ScribanTemplateRenderer handles nested objects.
/// </summary>
public sealed class ScribanTemplateRendererTests
{
    private readonly ScribanTemplateRenderer _sut = new(NullLogger<ScribanTemplateRenderer>.Instance);

    [Fact]
    public async Task RenderAsync_SimpleVariables_RendersCorrectly()
    {
        // TC-NOT-I001: renderer uses member.Name.ToLowerInvariant() renamer.
        // Use single-word properties to avoid ambiguity with Scriban's import behaviour.
        var template = "Xin chào {{ name }}, đơn {{ code }} đã được tiếp nhận.";
        var model = new { Name = "Nguyễn Văn A", Code = "HS-2024-001" };

        var result = await _sut.RenderAsync(template, model);

        result.Should().Contain("Nguyễn Văn A");
        result.Should().Contain("HS-2024-001");
    }

    [Fact]
    public async Task RenderAsync_MissingVariable_RendersEmptyStringNotException()
    {
        // TC-NOT-I002: missing variable → Scriban renders empty string, no throw
        var template = "Xin chào {{ missing_variable }}!";
        var model = new { Name = "Test" };

        var act = async () => await _sut.RenderAsync(template, model);

        await act.Should().NotThrowAsync();
        var result = await _sut.RenderAsync(template, model);
        result.Should().Contain("Xin chào");
    }

    [Fact]
    public async Task RenderAsync_NestedObject_AccessesNestedProperties()
    {
        // TC-NOT-I003: nested objects accessible via dot notation.
        // Using single-word property names to avoid Scriban import renaming edge cases.
        // The template uses {{user.name}} — 'user' maps to the User property (lowercased),
        // 'name' maps to the Name property (lowercased).
        var template = "Mã: {{ user.code }}";
        var model = new { User = new { Code = "USR-001" } };

        var result = await _sut.RenderAsync(template, model);

        result.Should().Contain("USR-001");
    }

    [Fact]
    public async Task RenderAsync_StaticText_ReturnsUnchanged()
    {
        var template = "Thông báo hệ thống — không có biến.";
        var model = new { };

        var result = await _sut.RenderAsync(template, model);

        result.Should().Be("Thông báo hệ thống — không có biến.");
    }

    [Fact]
    public async Task RenderAsync_InvalidTemplate_ThrowsInvalidOperationException()
    {
        // Scriban parse errors → InvalidOperationException from renderer
        var badTemplate = "{{ if }}"; // incomplete if block

        var act = async () => await _sut.RenderAsync(badTemplate, new { });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Template parse error*");
    }

    [Fact]
    public async Task RenderAsync_ConditionalLogic_EvaluatesCorrectly()
    {
        var template = "{{ if isurgent }}KHẨN: {{ end }}{{ title }}";
        var model = new { IsUrgent = true, Title = "Yêu cầu xử lý" };

        var result = await _sut.RenderAsync(template, model);

        result.Should().Contain("KHẨN:");
        result.Should().Contain("Yêu cầu xử lý");
    }
}
