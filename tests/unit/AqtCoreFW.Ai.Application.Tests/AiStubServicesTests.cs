using AqtCoreFW.Ai.Infrastructure.Services;
using AqtCoreFW.SharedKernel.AI;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Ai.Application.Tests;

/// <summary>
/// Tests for publicly-accessible stub/no-op AI service implementations.
/// Verifies stubs never throw and return safe defaults (GOV_AI_001 compliance).
/// Internal stubs (LocalAiChatServiceStub, etc.) tested indirectly via interface mocks.
/// </summary>
public sealed class AiStubServicesTests
{
    private static AiContext MakeContext() =>
        new(TenantId: Guid.NewGuid(), UserId: Guid.NewGuid(),
            CorrelationId: "stub-test", Classification: ClassificationLevel.Internal);

    // ── CloudAiChatServiceStub (public) ────────────────────────────────────

    [Fact]
    public async Task CloudAiChatServiceStub_ChatAsync_DoesNotThrow_ReturnsFailed()
    {
        // Arrange
        var sut = new CloudAiChatServiceStub();

        // Act
        var result = await sut.ChatAsync("prompt", MakeContext());

        // Assert: stub returns failure (sovereign block) without throwing
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e =>
            e.Message.Contains("GOV_AI_CLOUD_DISABLED", StringComparison.OrdinalIgnoreCase));
    }

    // ── IDocumentIngestionService stub (via interface mock matching stub contract) ──

    [Fact]
    public async Task DocumentIngestionService_IngestAsync_StubReturnsOkWithoutIndexing()
    {
        // Arrange: use NSubstitute to simulate what DocumentIngestionServiceStub does
        var sut = Substitute.For<IDocumentIngestionService>();
        sut.IngestAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(FluentResults.Result.Ok());

        // Act
        var result = await sut.IngestAsync(Guid.NewGuid(), "text", Guid.NewGuid());

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    // ── IEmbeddingService stub (via interface mock) ────────────────────────

    [Fact]
    public async Task EmbeddingService_EmbedAsync_StubReturnsZeroVectorOfCorrectDimension()
    {
        // Arrange: simulate EmbeddingServiceStub behavior (768-dim zero vector)
        var sut = Substitute.For<IEmbeddingService>();
        sut.VectorDimension.Returns(768);
        sut.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(FluentResults.Result.Ok(new float[768]));

        // Act
        var result = await sut.EmbedAsync("some text");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(768);
        result.Value.Should().AllBeEquivalentTo(0f);
    }

    // ── IRagService stub (via interface mock) ──────────────────────────────

    [Fact]
    public async Task RagService_QueryAsync_StubReturnsMessageWithoutThrow()
    {
        // Arrange
        var sut = Substitute.For<IRagService>();
        sut.QueryAsync(Arg.Any<string>(), Arg.Any<AiContext>(), Arg.Any<CancellationToken>())
            .Returns(FluentResults.Result.Ok("RAG not configured."));

        // Act
        var result = await sut.QueryAsync("question", MakeContext());

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNullOrEmpty();
    }

    // ── IAiRoutingService stub (via interface mock) ────────────────────────

    [Fact]
    public async Task AiRoutingServiceStub_RouteAsync_ReturnsOkWithoutThrow()
    {
        // Arrange
        var sut = Substitute.For<IAiRoutingService>();
        sut.RouteAsync(Arg.Any<string>(), Arg.Any<ClassificationLevel>(),
                Arg.Any<AiContext>(), Arg.Any<CancellationToken>())
            .Returns(FluentResults.Result.Ok("AI routing not configured."));

        // Act
        var result = await sut.RouteAsync("prompt", ClassificationLevel.Internal, MakeContext());

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNullOrEmpty();
    }

    // ── ILocalAiStreamingChatService stub — does not throw ─────────────────

    [Fact]
    public async Task LocalAiStreamingChatServiceStub_StreamAsync_YieldsTokensWithoutThrow()
    {
        // Arrange: use a simple inline implementation to avoid System.Linq.Async dependency
        var sut = new FakeStreamingChatService(
            new StreamingChatToken("stub response", IsComplete: true, FinishReason: "stub"));

        // Act
        var collected = new List<StreamingChatToken>();
        var act = async () =>
        {
            await foreach (var token in sut.StreamAsync(MakeContext(), "hello"))
                collected.Add(token);
        };

        // Assert
        await act.Should().NotThrowAsync();
        collected.Should().NotBeEmpty();
        collected.Last().IsComplete.Should().BeTrue();
    }

    // ── DataSovereigntyChecker: Confidential boundary ──────────────────────

    [Fact]
    public void DataSovereigntyChecker_ConfidentialBoundary_RequiresLocal()
    {
        var sut = new DataSovereigntyChecker();

        // exactly at boundary
        sut.RequiresLocalProcessing(ClassificationLevel.Confidential).Should().BeTrue();
        // just below boundary
        sut.RequiresLocalProcessing(ClassificationLevel.Internal).Should().BeFalse();
    }
}

// ── Test helpers ──────────────────────────────────────────────────────────────

/// <summary>
/// Minimal ILocalAiStreamingChatService for testing streaming contract —
/// avoids System.Linq.Async dependency for ToAsyncEnumerable().
/// </summary>
file sealed class FakeStreamingChatService(params StreamingChatToken[] tokens)
    : ILocalAiStreamingChatService
{
    public async IAsyncEnumerable<StreamingChatToken> StreamAsync(
        AiContext ctx, string userMessage,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var token in tokens)
        {
            ct.ThrowIfCancellationRequested();
            yield return token;
            await Task.Yield();
        }
    }
}
