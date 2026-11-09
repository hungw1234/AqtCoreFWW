using AqtCoreFW.Ai.Infrastructure.Services;
using AqtCoreFW.SharedKernel.AI;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AqtCoreFW.Ai.Application.Tests;

/// <summary>
/// Unit tests for OllamaAutoFlaggingService.
/// Verifies: empty-content fast path, JSON parsing (clean + markdown-wrapped),
/// unknown risk level normalisation, timeout graceful degradation, Ollama-down fallback.
/// </summary>
public sealed class AiAutoFlaggingServiceTests
{
    private readonly IChatClient _chatClient = Substitute.For<IChatClient>();
    private readonly Guid _tenantId = Guid.NewGuid();

    private OllamaAutoFlaggingService CreateSut() =>
        new(_chatClient, NullLogger<OllamaAutoFlaggingService>.Instance);

    // ── Empty / null content ──────────────────────────────────────────────────

    [Fact]
    public async Task FlagAsync_EmptyContent_ReturnsLowRisk_WithoutCallingLlm()
    {
        var sut = CreateSut();

        var result = await sut.FlagAsync(string.Empty, _tenantId);

        result.RiskLevel.Should().Be("low");
        result.DetectedFlags.Should().BeEmpty();
        await _chatClient.DidNotReceive()
            .GetResponseAsync(Arg.Any<IList<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FlagAsync_WhitespaceContent_ReturnsLowRisk_WithoutCallingLlm()
    {
        var sut = CreateSut();

        var result = await sut.FlagAsync("   ", _tenantId);

        result.RiskLevel.Should().Be("low");
        await _chatClient.DidNotReceive()
            .GetResponseAsync(Arg.Any<IList<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>());
    }

    // ── Valid JSON response ───────────────────────────────────────────────────

    [Fact]
    public async Task FlagAsync_ValidJson_ParsesRiskLevelAndFlags()
    {
        var sut = CreateSut();
        SetupLlmResponse("""{"riskLevel":"high","detectedFlags":["pii","financial"],"explanation":"Contains CCCD number."}""");

        var result = await sut.FlagAsync("CCCD: 123456789012, salary: 20M VND", _tenantId);

        result.RiskLevel.Should().Be("high");
        result.DetectedFlags.Should().BeEquivalentTo(["pii", "financial"]);
        result.Explanation.Should().Be("Contains CCCD number.");
    }

    [Fact]
    public async Task FlagAsync_CriticalRisk_ReturnsCorrectly()
    {
        var sut = CreateSut();
        SetupLlmResponse("""{"riskLevel":"critical","detectedFlags":["sensitive_data","policy_violation"],"explanation":"State secret."}""");

        var result = await sut.FlagAsync("Top secret government document", _tenantId);

        result.RiskLevel.Should().Be("critical");
        result.DetectedFlags.Should().Contain("sensitive_data").And.Contain("policy_violation");
    }

    // ── Markdown-wrapped JSON (LLM wraps in code fences) ─────────────────────

    [Fact]
    public async Task FlagAsync_MarkdownWrappedJson_ParsesCorrectly()
    {
        var sut = CreateSut();
        SetupLlmResponse("```json\n{\"riskLevel\":\"medium\",\"detectedFlags\":[\"legal\"],\"explanation\":\"Contract terms.\"}\n```");

        var result = await sut.FlagAsync("This contract binds both parties.", _tenantId);

        result.RiskLevel.Should().Be("medium");
        result.DetectedFlags.Should().BeEquivalentTo(["legal"]);
    }

    [Fact]
    public async Task FlagAsync_PlainCodeFenceJson_ParsesCorrectly()
    {
        var sut = CreateSut();
        SetupLlmResponse("```\n{\"riskLevel\":\"low\",\"detectedFlags\":[],\"explanation\":\"Benign.\"}\n```");

        var result = await sut.FlagAsync("Hello world", _tenantId);

        result.RiskLevel.Should().Be("low");
        result.DetectedFlags.Should().BeEmpty();
    }

    // ── Normalisation of unknown risk level ───────────────────────────────────

    [Fact]
    public async Task FlagAsync_UnknownRiskLevel_NormalisesToLow()
    {
        var sut = CreateSut();
        SetupLlmResponse("""{"riskLevel":"unknown_level","detectedFlags":[],"explanation":"LLM hallucination."}""");

        var result = await sut.FlagAsync("Some text", _tenantId);

        result.RiskLevel.Should().Be("low");
    }

    // ── Malformed JSON ────────────────────────────────────────────────────────

    [Fact]
    public async Task FlagAsync_MalformedJson_ReturnsLowRiskFallback()
    {
        var sut = CreateSut();
        SetupLlmResponse("this is not json at all");

        var result = await sut.FlagAsync("Some content", _tenantId);

        result.RiskLevel.Should().Be("low");
        result.DetectedFlags.Should().BeEmpty();
    }

    [Fact]
    public async Task FlagAsync_EmptyLlmResponse_ReturnsLowRiskFallback()
    {
        var sut = CreateSut();
        SetupLlmResponse(string.Empty);

        var result = await sut.FlagAsync("Some content", _tenantId);

        result.RiskLevel.Should().Be("low");
    }

    // ── Graceful degradation: Ollama unavailable ──────────────────────────────

    [Fact]
    public async Task FlagAsync_OllamaThrows_ReturnsLowRiskFallback()
    {
        var sut = CreateSut();
        _chatClient
            .GetResponseAsync(Arg.Any<IList<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Throws(new HttpRequestException("Connection refused"));

        var result = await sut.FlagAsync("Some content", _tenantId);

        result.RiskLevel.Should().Be("low");
        result.DetectedFlags.Should().BeEmpty();
        result.Explanation.Should().Contain("unavailable");
    }

    [Fact]
    public async Task FlagAsync_OllamaThrows_DoesNotPropagateException()
    {
        var sut = CreateSut();
        _chatClient
            .GetResponseAsync(Arg.Any<IList<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("GPU out of memory"));

        var act = () => sut.FlagAsync("Any content", _tenantId);

        await act.Should().NotThrowAsync();
    }

    // ── Stub ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Stub_AlwaysReturnsLowRisk()
    {
        var stub = new AiAutoFlaggingServiceStub();

        var result = await stub.FlagAsync("classified document", _tenantId);

        result.RiskLevel.Should().Be("low");
        result.DetectedFlags.Should().BeEmpty();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetupLlmResponse(string text)
    {
        var response = new ChatResponse([new ChatMessage(ChatRole.Assistant, text)]);
        _chatClient
            .GetResponseAsync(Arg.Any<IList<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));
    }
}
