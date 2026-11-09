using AqtCoreFW.Ai.Infrastructure.Services;
using AqtCoreFW.SharedKernel.AI;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AqtCoreFW.Ai.Application.Tests;

/// <summary>
/// Tests AiRoutingService data sovereignty enforcement (GOV_AI_001).
/// Uses NSubstitute for ILocalAiChatService and ICloudAiChatService to avoid
/// dependency on internal stub types.
/// </summary>
public sealed class AiRoutingServiceTests
{
    private static AiContext MakeContext(ClassificationLevel level) =>
        new(TenantId: Guid.NewGuid(), UserId: Guid.NewGuid(),
            CorrelationId: "test-corr", Classification: level);

    // ── DataSovereigntyChecker ──────────────────────────────────────────────

    [Theory]
    [InlineData(ClassificationLevel.Confidential, true)]
    [InlineData(ClassificationLevel.Secret, true)]
    [InlineData(ClassificationLevel.TopSecret, true)]
    [InlineData(ClassificationLevel.Public, false)]
    [InlineData(ClassificationLevel.Internal, false)]
    public void DataSovereigntyChecker_RequiresLocalProcessing_CorrectForAllLevels(
        ClassificationLevel level, bool expected)
    {
        // Arrange
        var checker = new DataSovereigntyChecker();

        // Act
        var result = checker.RequiresLocalProcessing(level);

        // Assert
        result.Should().Be(expected);
    }

    // ── AiRoutingService: Confidential → routes to local ───────────────────

    [Theory]
    [InlineData(ClassificationLevel.Confidential)]
    [InlineData(ClassificationLevel.Secret)]
    [InlineData(ClassificationLevel.TopSecret)]
    public async Task RouteAsync_ConfidentialOrAbove_RoutesToLocalOnly(ClassificationLevel level)
    {
        // Arrange
        var localChat = Substitute.For<ILocalAiChatService>();
        var cloudChat = Substitute.For<ICloudAiChatService>();
        var checker = new DataSovereigntyChecker();
        var logger = NullLogger<AiRoutingService>.Instance;

        localChat.ChatAsync(Arg.Any<string>(), Arg.Any<AiContext>(), Arg.Any<CancellationToken>())
            .Returns(FluentResults.Result.Ok("Local AI response"));

        var sut = new AiRoutingService(localChat, cloudChat, checker, logger);
        var ctx = MakeContext(level);

        // Act
        var result = await sut.RouteAsync("test prompt", level, ctx);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Local AI response");
        await cloudChat.DidNotReceive().ChatAsync(
            Arg.Any<string>(), Arg.Any<AiContext>(), Arg.Any<CancellationToken>());
    }

    // ── AiRoutingService: GOV_AI_001 when local = stub + classification >= Confidential ──

    [Theory]
    [InlineData(ClassificationLevel.Confidential)]
    [InlineData(ClassificationLevel.Secret)]
    public async Task RouteAsync_LocalIsStub_ConfidentialClassification_ReturnsGovAi001Error(
        ClassificationLevel level)
    {
        // Arrange: local stub returns "AI not configured" — simulates stub behavior
        var localChat = Substitute.For<ILocalAiChatService>();
        var cloudChat = Substitute.For<ICloudAiChatService>();
        var checker = new DataSovereigntyChecker();
        var logger = NullLogger<AiRoutingService>.Instance;

        localChat.ChatAsync(Arg.Any<string>(), Arg.Any<AiContext>(), Arg.Any<CancellationToken>())
            .Returns(FluentResults.Result.Ok("AI not configured. Set AI:Ollama:Enabled=true."));

        var sut = new AiRoutingService(localChat, cloudChat, checker, logger);
        var ctx = MakeContext(level);

        // Act
        var result = await sut.RouteAsync("sensitive prompt", level, ctx);

        // Assert: GOV_AI_001 sovereignty violation returned
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e =>
            e.Message.Contains("GOV_AI_001", StringComparison.OrdinalIgnoreCase));
    }

    // ── AiRoutingService: Public/Internal → tries cloud, falls back to local ──

    [Theory]
    [InlineData(ClassificationLevel.Public)]
    [InlineData(ClassificationLevel.Internal)]
    public async Task RouteAsync_PublicOrInternal_TriesCloudFirst(ClassificationLevel level)
    {
        // Arrange
        var localChat = Substitute.For<ILocalAiChatService>();
        var cloudChat = Substitute.For<ICloudAiChatService>();
        var checker = new DataSovereigntyChecker();
        var logger = NullLogger<AiRoutingService>.Instance;

        cloudChat.ChatAsync(Arg.Any<string>(), Arg.Any<AiContext>(), Arg.Any<CancellationToken>())
            .Returns(FluentResults.Result.Ok("Cloud AI response"));

        var sut = new AiRoutingService(localChat, cloudChat, checker, logger);
        var ctx = MakeContext(level);

        // Act
        var result = await sut.RouteAsync("public prompt", level, ctx);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Cloud AI response");
        await localChat.DidNotReceive().ChatAsync(
            Arg.Any<string>(), Arg.Any<AiContext>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(ClassificationLevel.Public)]
    [InlineData(ClassificationLevel.Internal)]
    public async Task RouteAsync_CloudFails_FallsBackToLocal(ClassificationLevel level)
    {
        // Arrange
        var localChat = Substitute.For<ILocalAiChatService>();
        var cloudChat = Substitute.For<ICloudAiChatService>();
        var checker = new DataSovereigntyChecker();
        var logger = NullLogger<AiRoutingService>.Instance;

        cloudChat.ChatAsync(Arg.Any<string>(), Arg.Any<AiContext>(), Arg.Any<CancellationToken>())
            .Returns(FluentResults.Result.Fail<string>("GOV_AI_CLOUD_DISABLED"));

        localChat.ChatAsync(Arg.Any<string>(), Arg.Any<AiContext>(), Arg.Any<CancellationToken>())
            .Returns(FluentResults.Result.Ok("Local fallback response"));

        var sut = new AiRoutingService(localChat, cloudChat, checker, logger);
        var ctx = MakeContext(level);

        // Act
        var result = await sut.RouteAsync("public prompt", level, ctx);

        // Assert: local fallback used
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Local fallback response");
    }
}
