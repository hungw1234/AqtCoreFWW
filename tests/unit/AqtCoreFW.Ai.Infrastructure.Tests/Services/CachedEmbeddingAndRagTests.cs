using AqtCoreFW.Ai.Infrastructure.Services;
using AqtCoreFW.Ai.Infrastructure.VectorStore;
using AqtCoreFW.SharedKernel.AI;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using FluentResults;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Text.Json;
using InfraVectorSearchResult = AqtCoreFW.Ai.Infrastructure.VectorStore.VectorSearchResult;

namespace AqtCoreFW.Ai.Infrastructure.Tests.Services;

/// <summary>
/// TC-AI-I003: CachedEmbeddingService returns cached embedding on repeat call.
/// TC-AI-I005: RagService combines context with query and routes through IAiRoutingService.
/// </summary>
public sealed class CachedEmbeddingAndRagTests
{
    // --- CachedEmbeddingService (TC-AI-I003) ---

    [Fact]
    public async Task EmbedAsync_CacheHit_ReturnsCachedVectorWithoutCallingInner()
    {
        // TC-AI-I003: cache hit → inner service never called
        var inner = Substitute.For<IEmbeddingService>();
        inner.VectorDimension.Returns(384);
        var cache = Substitute.For<IDistributedCache>();

        var cachedVector = new float[] { 0.1f, 0.2f, 0.3f };
        var cachedBytes = JsonSerializer.SerializeToUtf8Bytes(cachedVector);
        cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<byte[]?>(cachedBytes));

        var sut = new CachedEmbeddingService(inner, cache, NullLogger<CachedEmbeddingService>.Instance);

        var result = await sut.EmbedAsync("some text");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(cachedVector);
        await inner.DidNotReceive().EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EmbedAsync_CacheMiss_CallsInnerAndPopulatesCache()
    {
        // TC-AI-I003: cache miss → inner called → cache populated for next call
        var expectedVector = new float[] { 1.0f, 2.0f };
        var inner = Substitute.For<IEmbeddingService>();
        inner.VectorDimension.Returns(2);
        inner.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(expectedVector)));

        var cache = Substitute.For<IDistributedCache>();
        cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<byte[]?>(null)); // cache miss

        var sut = new CachedEmbeddingService(inner, cache, NullLogger<CachedEmbeddingService>.Instance);

        var result = await sut.EmbedAsync("text");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(expectedVector);
        await inner.Received(1).EmbedAsync("text", Arg.Any<CancellationToken>());
        await cache.Received(1).SetAsync(
            Arg.Any<string>(), Arg.Any<byte[]>(),
            Arg.Any<DistributedCacheEntryOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EmbedAsync_VectorDimension_DelegatedToInner()
    {
        var inner = Substitute.For<IEmbeddingService>();
        inner.VectorDimension.Returns(1024);
        var cache = Substitute.For<IDistributedCache>();
        cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<byte[]?>(null));
        inner.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(new float[1024])));

        var sut = new CachedEmbeddingService(inner, cache, NullLogger<CachedEmbeddingService>.Instance);

        sut.VectorDimension.Should().Be(1024);
    }

    [Fact]
    public async Task EmbedAsync_InnerFails_ReturnsFail()
    {
        var inner = Substitute.For<IEmbeddingService>();
        inner.VectorDimension.Returns(384);
        inner.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Fail<float[]>("model error")));
        var cache = Substitute.For<IDistributedCache>();
        cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<byte[]?>(null));

        var sut = new CachedEmbeddingService(inner, cache, NullLogger<CachedEmbeddingService>.Instance);

        var result = await sut.EmbedAsync("text");

        result.IsFailed.Should().BeTrue();
    }

    // --- RagService (TC-AI-I005) ---

    [Fact]
    public async Task QueryAsync_EmbeddingSucceeds_BuildsAugmentedPromptAndRoutes()
    {
        // TC-AI-I005: RAG retrieves context and combines it with question before routing
        var embedding = new float[] { 0.5f, 0.5f };
        var embeddingService = Substitute.For<IEmbeddingService>();
        embeddingService.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(embedding)));

        var searchResults = new List<InfraVectorSearchResult>
        {
            new("doc-1", 0.85f, "Điều 5: Quy định về xử lý hồ sơ trong 3 ngày",
                new Dictionary<string, string> { ["tenant_id"] = "t1" })
        };
        var vectorStore = Substitute.For<IQdrantVectorStore>();
        vectorStore.SearchAsync(Arg.Any<string>(), Arg.Any<float[]>(),
                Arg.Any<int>(), Arg.Any<float>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<InfraVectorSearchResult>>(searchResults));

        var routingService = Substitute.For<IAiRoutingService>();
        routingService.RouteAsync(Arg.Any<string>(), Arg.Any<ClassificationLevel>(),
                Arg.Any<AiContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok("Augmented answer")));

        var sut = new RagService(
            embeddingService, vectorStore, routingService,
            NullLogger<RagService>.Instance);

        var ctx = new AiContext(Guid.NewGuid(), Guid.NewGuid(), "corr-1", ClassificationLevel.Internal);

        var result = await sut.QueryAsync("Hồ sơ xử lý bao lâu?", ctx);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Augmented answer");

        // Verify routing used an augmented prompt (contains context markers)
        await routingService.Received(1).RouteAsync(
            Arg.Is<string>(p => p.Contains("TÀI LIỆU THAM KHẢO") && p.Contains("CÂU HỎI")),
            Arg.Any<ClassificationLevel>(),
            Arg.Any<AiContext>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QueryAsync_EmbeddingFails_RoutesOriginalQuestionWithoutContext()
    {
        // TC-AI-I005 degraded path: embedding failure → original question routed without context
        var embeddingService = Substitute.For<IEmbeddingService>();
        embeddingService.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Fail<float[]>("Embedding unavailable")));

        var vectorStore = Substitute.For<IQdrantVectorStore>();
        var routingService = Substitute.For<IAiRoutingService>();
        routingService.RouteAsync(Arg.Any<string>(), Arg.Any<ClassificationLevel>(),
                Arg.Any<AiContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok("Raw answer")));

        var sut = new RagService(
            embeddingService, vectorStore, routingService,
            NullLogger<RagService>.Instance);

        var ctx = new AiContext(Guid.NewGuid(), Guid.NewGuid(), "corr-1", ClassificationLevel.Internal);

        var result = await sut.QueryAsync("My question?", ctx);

        result.IsSuccess.Should().BeTrue();
        await routingService.Received(1).RouteAsync(
            "My question?",
            Arg.Any<ClassificationLevel>(),
            Arg.Any<AiContext>(),
            Arg.Any<CancellationToken>());
    }
}
