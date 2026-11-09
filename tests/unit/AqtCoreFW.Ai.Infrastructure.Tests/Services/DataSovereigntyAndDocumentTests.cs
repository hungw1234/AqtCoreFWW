using AqtCoreFW.Ai.Infrastructure.Services;
using AqtCoreFW.Ai.Infrastructure.VectorStore;
using AqtCoreFW.SharedKernel.AI;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AqtCoreFW.Ai.Infrastructure.Tests.Services;

/// <summary>
/// TC-AI-I001: DataSovereigntyChecker blocks foreign endpoints (Confidential/Secret/TopSecret).
/// TC-AI-I002: DataSovereigntyChecker allows domestic (Public/Internal).
/// TC-AI-I004: DocumentIngestionService chunks text correctly — verified via public IngestAsync behaviour.
/// </summary>
public sealed class DataSovereigntyAndDocumentTests
{
    // --- DataSovereigntyChecker (TC-AI-I001, TC-AI-I002) ---

    private readonly DataSovereigntyChecker _checker = new();

    [Theory]
    [InlineData(ClassificationLevel.Confidential)]
    [InlineData(ClassificationLevel.Secret)]
    [InlineData(ClassificationLevel.TopSecret)]
    public void DataSovereigntyChecker_HighClassification_RequiresLocalProcessing(ClassificationLevel level)
    {
        // TC-AI-I001: foreign/cloud endpoints blocked for Confidential and above
        _checker.RequiresLocalProcessing(level).Should().BeTrue();
    }

    [Theory]
    [InlineData(ClassificationLevel.Public)]
    [InlineData(ClassificationLevel.Internal)]
    public void DataSovereigntyChecker_LowClassification_DoesNotRequireLocalProcessing(ClassificationLevel level)
    {
        // TC-AI-I002: Public and Internal data may use cloud endpoints
        _checker.RequiresLocalProcessing(level).Should().BeFalse();
    }

    [Fact]
    public void DataSovereigntyChecker_ConfidentialBoundary_IsExactlyAtConfidential()
    {
        _checker.RequiresLocalProcessing(ClassificationLevel.Internal).Should().BeFalse();
        _checker.RequiresLocalProcessing(ClassificationLevel.Confidential).Should().BeTrue();
    }

    // --- DocumentIngestionService (TC-AI-I004) ---
    // ChunkText is internal — test via public IngestAsync observable behavior:
    // short text (< 50 chars per paragraph) → no embeddings called (all chunks filtered)
    // normal text → embeddings called per chunk

    [Fact]
    public async Task IngestAsync_AllParagraphsTooShort_NoEmbeddingCallsMade()
    {
        // TC-AI-I004: paragraphs below 50 chars are filtered → zero embed calls
        var embeddingService = Substitute.For<IEmbeddingService>();
        embeddingService.VectorDimension.Returns(384);

        var vectorStore = Substitute.For<IQdrantVectorStore>();
        vectorStore.EnsureCollectionAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok()));

        var sut = new DocumentIngestionService(
            embeddingService, vectorStore,
            NullLogger<DocumentIngestionService>.Instance);

        // All paragraphs < 50 chars — no chunks produced
        var text = "Short.\n\nAnother short.";
        var result = await sut.IngestAsync(Guid.NewGuid(), text, Guid.NewGuid());

        result.IsSuccess.Should().BeTrue();
        await embeddingService.DidNotReceive().EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_ValidParagraph_CallsEmbedOnce()
    {
        // TC-AI-I004: a single valid paragraph (>= 50 chars) → one embed call
        var embeddingService = Substitute.For<IEmbeddingService>();
        embeddingService.VectorDimension.Returns(384);
        embeddingService.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(new float[384])));

        var vectorStore = Substitute.For<IQdrantVectorStore>();
        vectorStore.EnsureCollectionAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok()));
        vectorStore.UpsertAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<float[]>(),
                Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok()));

        var sut = new DocumentIngestionService(
            embeddingService, vectorStore,
            NullLogger<DocumentIngestionService>.Instance);

        var validParagraph = new string('X', 100); // 100 chars > 50 min
        var result = await sut.IngestAsync(Guid.NewGuid(), validParagraph, Guid.NewGuid());

        result.IsSuccess.Should().BeTrue();
        await embeddingService.Received(1).EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_TwoParagraphs_CallsEmbedTwice()
    {
        // TC-AI-I004: two valid paragraphs → two embed calls
        var embeddingService = Substitute.For<IEmbeddingService>();
        embeddingService.VectorDimension.Returns(384);
        embeddingService.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(new float[384])));

        var vectorStore = Substitute.For<IQdrantVectorStore>();
        vectorStore.EnsureCollectionAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok()));
        vectorStore.UpsertAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<float[]>(),
                Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok()));

        var sut = new DocumentIngestionService(
            embeddingService, vectorStore,
            NullLogger<DocumentIngestionService>.Instance);

        var para1 = new string('A', 100);
        var para2 = new string('B', 200);
        var text = $"{para1}\n\n{para2}";

        var result = await sut.IngestAsync(Guid.NewGuid(), text, Guid.NewGuid());

        result.IsSuccess.Should().BeTrue();
        await embeddingService.Received(2).EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_EmbeddingFails_AbortsHardAndDoesNotUpsert()
    {
        // TC-AI-I004: a failed embedding chunk must abort the whole ingestion (all-or-nothing) —
        // never silently skipped-and-reported-success (would leave a partially indexed document).
        var embeddingService = Substitute.For<IEmbeddingService>();
        embeddingService.VectorDimension.Returns(384);
        embeddingService.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Fail<float[]>("embedding model unavailable")));

        var vectorStore = Substitute.For<IQdrantVectorStore>();
        vectorStore.EnsureCollectionAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok()));

        var sut = new DocumentIngestionService(
            embeddingService, vectorStore,
            NullLogger<DocumentIngestionService>.Instance);

        var para = new string('X', 100);
        var result = await sut.IngestAsync(Guid.NewGuid(), para, Guid.NewGuid());

        result.IsFailed.Should().BeTrue();
        result.Errors[0].Message.Should().Contain("embedding model unavailable");
        // Never reach the vector store when an embedding fails
        await vectorStore.DidNotReceive().UpsertAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<float[]>(),
            Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_VectorStoreDown_ReturnsFailure()
    {
        // TC-AI-I004: Qdrant down → hard failure (GOV_AI_003)
        var embeddingService = Substitute.For<IEmbeddingService>();
        embeddingService.VectorDimension.Returns(384);
        embeddingService.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(new float[384])));

        var vectorStore = Substitute.For<IQdrantVectorStore>();
        vectorStore.EnsureCollectionAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok()));
        vectorStore.UpsertAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<float[]>(),
                Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Fail("Qdrant unavailable")));

        var sut = new DocumentIngestionService(
            embeddingService, vectorStore,
            NullLogger<DocumentIngestionService>.Instance);

        var para = new string('X', 100);
        var result = await sut.IngestAsync(Guid.NewGuid(), para, Guid.NewGuid());

        result.IsFailed.Should().BeTrue();
        result.Errors[0].Message.Should().Contain("GOV_AI_003");
    }
}
