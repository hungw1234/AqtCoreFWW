using AqtCoreFW.Ai.Infrastructure.Services;
using AqtCoreFW.SharedKernel.AI;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AqtCoreFW.Ai.Application.Tests;

/// <summary>
/// Unit tests for OllamaDocumentExtractor.
/// IChatClient is mocked — no real Ollama instance needed.
/// Covers: null/empty bytes, unsupported mime type, JSON parse, timeout, markdown code fences.
/// </summary>
public sealed class OllamaDocumentExtractorTests
{
    private readonly IChatClient _chatClient = Substitute.For<IChatClient>();
    private readonly OllamaDocumentExtractor _sut;

    public OllamaDocumentExtractorTests()
    {
        _sut = new OllamaDocumentExtractor(
            _chatClient,
            NullLogger<OllamaDocumentExtractor>.Instance);
    }

    // ── Guard: null / empty bytes ─────────────────────────────────────────────

    [Fact]
    public async Task ExtractAsync_NullBytes_ReturnsFail()
    {
        var result = await _sut.ExtractAsync(null!, "image/png");

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("GOV_AI_OCR_PARSE"));
    }

    [Fact]
    public async Task ExtractAsync_EmptyBytes_ReturnsFail()
    {
        var result = await _sut.ExtractAsync([], "image/jpeg");

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("GOV_AI_OCR_PARSE"));
    }

    // ── Guard: unsupported mime type ──────────────────────────────────────────

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/zip")]
    [InlineData("video/mp4")]
    public async Task ExtractAsync_UnsupportedMimeType_ReturnsFail(string mimeType)
    {
        var result = await _sut.ExtractAsync([0x01, 0x02], mimeType);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e =>
            e.Message.Contains("GOV_AI_OCR_PARSE") && e.Message.Contains("Unsupported mime type"));
    }

    // ── Supported mime types pass validation ──────────────────────────────────

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/webp")]
    [InlineData("application/pdf")]
    public async Task ExtractAsync_SupportedMimeType_CallsChatClient(string mimeType)
    {
        // Arrange: LLM returns valid JSON
        SetupChatResponse("""{"title":"T","content":"C","fields":{}}""");

        var result = await _sut.ExtractAsync([0xFF, 0xD8], mimeType);

        result.IsSuccess.Should().BeTrue();
        await _chatClient.Received(1).GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>());
    }

    // ── Happy path: valid JSON response ───────────────────────────────────────

    [Fact]
    public async Task ExtractAsync_ValidJsonResponse_ReturnsExtractedDocument()
    {
        SetupChatResponse("""
            {
              "title": "CCCD",
              "content": "Nguyen Van A, born 1990",
              "fields": { "name": "Nguyen Van A", "dob": "1990-01-01" }
            }
            """);

        var result = await _sut.ExtractAsync([0x89, 0x50], "image/png");

        result.IsSuccess.Should().BeTrue();
        result.Value.Title.Should().Be("CCCD");
        result.Value.Content.Should().Contain("Nguyen Van A");
        result.Value.Metadata.Should().ContainKey("name");
        result.Value.Metadata["dob"].Should().Be("1990-01-01");
    }

    // ── Lenient parse: JSON wrapped in markdown code fences ───────────────────

    [Fact]
    public async Task ExtractAsync_MarkdownFencedJson_ParsesSuccessfully()
    {
        SetupChatResponse("```json\n{\"title\":\"Doc\",\"content\":\"text\",\"fields\":{}}\n```");

        var result = await _sut.ExtractAsync([0x01], "image/jpeg");

        result.IsSuccess.Should().BeTrue();
        result.Value.Title.Should().Be("Doc");
    }

    // ── Bad JSON → GOV_AI_OCR_PARSE ──────────────────────────────────────────

    [Fact]
    public async Task ExtractAsync_InvalidJson_ReturnsOcrParseFail()
    {
        SetupChatResponse("Sorry, I cannot process this image.");

        var result = await _sut.ExtractAsync([0x01], "image/png");

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("GOV_AI_OCR_PARSE"));
    }

    // ── Timeout → GOV_AI_002 ──────────────────────────────────────────────────

    [Fact]
    public async Task ExtractAsync_ChatClientThrowsOperationCanceled_ReturnsGovAi002()
    {
        _chatClient
            .GetResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns<ChatResponse>(_ => throw new OperationCanceledException());

        var result = await _sut.ExtractAsync([0x01], "image/png");

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("GOV_AI_002"));
    }

    // ── Network error → GOV_AI_002 ────────────────────────────────────────────

    [Fact]
    public async Task ExtractAsync_ChatClientThrowsHttpException_ReturnsGovAi002()
    {
        _chatClient
            .GetResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns<ChatResponse>(_ => throw new HttpRequestException("Connection refused"));

        var result = await _sut.ExtractAsync([0x01], "image/png");

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e =>
            e.Message.Contains("GOV_AI_002") && e.Message.Contains("Connection refused"));
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private void SetupChatResponse(string text)
    {
        var response = new ChatResponse([new ChatMessage(ChatRole.Assistant, text)]);
        _chatClient
            .GetResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(response);
    }
}
