using AqtCoreFW.Ai.Infrastructure.Services;
using AqtCoreFW.SharedKernel.AI;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AqtCoreFW.Ai.Application.Tests;

/// <summary>
/// Unit tests for OllamaReActAgent.
/// IChatClient is mocked — no real Ollama needed.
/// Covers: null task guard, final answer on first reply, max iterations guard,
///         tool call parsing, timeout handling.
/// </summary>
public sealed class OllamaReActAgentTests
{
    private readonly IChatClient _chatClient = Substitute.For<IChatClient>();
    private readonly OllamaReActAgent _sut;

    private static readonly Guid TenantId = Guid.NewGuid();

    public OllamaReActAgentTests()
    {
        _sut = new OllamaReActAgent(
            _chatClient,
            NullLogger<OllamaReActAgent>.Instance);
    }

    // ── Guard: null task ──────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_NullTask_ReturnsFailedResponse()
    {
        var result = await _sut.ExecuteAsync(null!);

        result.IsSuccess.Should().BeTrue(); // Result<AgentResponse> is Ok
        result.Value.Success.Should().BeFalse();
        result.Value.Output.Should().Contain("GOV_AI_AGENT");
    }

    // ── Happy path: LLM returns final answer immediately (no tool calls) ──────

    [Fact]
    public async Task ExecuteAsync_FinalAnswerOnFirstReply_ReturnsSuccessWithOutput()
    {
        // LLM returns plain text (no JSON tool call)
        SetupChatResponse("The case has been processed successfully.");

        var task = MakeTask("summarize_case", new() { ["case_id"] = "123" });
        var result = await _sut.ExecuteAsync(task);

        result.IsSuccess.Should().BeTrue();
        result.Value.Success.Should().BeTrue();
        result.Value.Output.Should().Be("The case has been processed successfully.");
        result.Value.Metadata.Should().ContainKey("iterations");
        result.Value.Metadata["iterations"].Should().Be("1");
    }

    // ── Max iterations guard ──────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_AlwaysReturnsToolCall_ExceedsMaxIterations()
    {
        // LLM always responds with a tool call JSON — never gives final answer
        var toolCallJson = """{"tool":"classify_text","args":{"text":"some text"}}""";
        SetupChatResponse(toolCallJson);

        var task = MakeTask("classify", new() { ["text"] = "sample" });
        var result = await _sut.ExecuteAsync(task);

        result.IsSuccess.Should().BeTrue(); // Result<AgentResponse> wraps the response
        result.Value.Success.Should().BeFalse();
        result.Value.Output.Should().Contain("GOV_AI_AGENT_MAX_ITER");
        result.Value.Metadata["error"].Should().Be("GOV_AI_AGENT_MAX_ITER");
        result.Value.Metadata["iterations"].Should().Be("5");
    }

    // ── Tool call: classify_text (pure function, no DB) ───────────────────────

    [Fact]
    public async Task ExecuteAsync_ClassifyTextToolCall_ThenFinalAnswer()
    {
        // First call: tool request; second call: final answer
        var callCount = 0;
        _chatClient
            .GetResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                var text = callCount == 1
                    ? """{"tool":"classify_text","args":{"text":"bí mật quốc gia"}}"""
                    : "The document is classified as Secret.";
                return new ChatResponse([new ChatMessage(ChatRole.Assistant, text)]);
            });

        var task = MakeTask("classify_document", new() { ["content"] = "bí mật quốc gia" });
        var result = await _sut.ExecuteAsync(task);

        result.Value.Success.Should().BeTrue();
        result.Value.Output.Should().Contain("classified as Secret");
        result.Value.Metadata["iterations"].Should().Be("2");
    }

    // ── Tool call: summarize (pure function, no DB) ───────────────────────────

    [Fact]
    public async Task ExecuteAsync_SummarizeToolCall_ThenFinalAnswer()
    {
        var callCount = 0;
        _chatClient
            .GetResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                var text = callCount == 1
                    ? """{"tool":"summarize","args":{"text":"This is a very long document about governance.","max_words":"5"}}"""
                    : "Summarized: This is a very";
                return new ChatResponse([new ChatMessage(ChatRole.Assistant, text)]);
            });

        var task = MakeTask("summarize_doc", new() { ["doc"] = "..." });
        var result = await _sut.ExecuteAsync(task);

        result.Value.Success.Should().BeTrue();
    }

    // ── Timeout → Result.Fail with GOV_AI_002 ────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ChatClientTimesOut_ReturnsFailResult()
    {
        _chatClient
            .GetResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns<ChatResponse>(_ => throw new OperationCanceledException());

        var task = MakeTask("any", new());
        var result = await _sut.ExecuteAsync(task);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("GOV_AI_002"));
    }

    // ── Network error → Result.Fail with GOV_AI_002 ──────────────────────────

    [Fact]
    public async Task ExecuteAsync_ChatClientThrows_ReturnsFailResult()
    {
        _chatClient
            .GetResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns<ChatResponse>(_ => throw new HttpRequestException("Connection refused"));

        var task = MakeTask("any", new());
        var result = await _sut.ExecuteAsync(task);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e =>
            e.Message.Contains("GOV_AI_002") && e.Message.Contains("Connection refused"));
    }

    // ── Unknown tool → error message fed back, agent continues ───────────────

    [Fact]
    public async Task ExecuteAsync_UnknownTool_FeedsErrorBackAndContinues()
    {
        var callCount = 0;
        _chatClient
            .GetResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                var text = callCount == 1
                    ? """{"tool":"nonexistent_tool","args":{}}"""
                    : "I will use a different approach.";
                return new ChatResponse([new ChatMessage(ChatRole.Assistant, text)]);
            });

        var task = MakeTask("test", new());
        var result = await _sut.ExecuteAsync(task);

        // Agent should recover and give final answer on iteration 2
        result.Value.Success.Should().BeTrue();
        result.Value.Output.Should().Contain("different approach");
    }

    // ── Markdown-fenced tool call JSON ────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_MarkdownFencedToolJson_ParsesAndExecutes()
    {
        var callCount = 0;
        _chatClient
            .GetResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                var text = callCount == 1
                    ? "```json\n{\"tool\":\"classify_text\",\"args\":{\"text\":\"public document\"}}\n```"
                    : "Classification complete.";
                return new ChatResponse([new ChatMessage(ChatRole.Assistant, text)]);
            });

        var task = MakeTask("classify", new());
        var result = await _sut.ExecuteAsync(task);

        result.Value.Success.Should().BeTrue();
        result.Value.Output.Should().Contain("Classification complete");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AgentTask MakeTask(string taskType, Dictionary<string, string> parameters) =>
        new(taskType, parameters, TenantId);

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
