using System.Security.Claims;
using AqtCoreFW.Collaboration.Domain.Entities;
using AqtCoreFW.Collaboration.Domain.Enums;
using AqtCoreFW.Collaboration.Domain.Repositories;
using AqtCoreFW.Collaboration.Infrastructure.Realtime;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Collaboration.Integration.Tests;

/// <summary>
/// Integration coverage for the <see cref="ChatHub.JoinConversation"/> IDOR gate against a real
/// database. The hub's security decision is delegated entirely to
/// <see cref="IConversationRepository.IsMemberAsync"/>, which runs with IgnoreQueryFilters and
/// re-applies tenant + soft-delete predicates by hand — semantics only a real SQL Server can verify.
///
/// SignalR has no transport in a test, so the real hub is driven directly: real repository + real DB,
/// with a synthetic <see cref="HubCallerContext"/> carrying the JWT claims and a substituted
/// <see cref="IGroupManager"/>. This exercises the full server-side path — claim extraction →
/// IsMemberAsync → throw/AddToGroup — that a mock-only unit test cannot reach.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class ChatHubMembershipTests : IClassFixture<WebAppFixture>
{
    private const string ConnectionId = "test-conn";
    private readonly WebAppFixture _app;

    public ChatHubMembershipTests(WebAppFixture app) => _app = app;

    /// <summary>Seeds a group conversation whose owner is its sole member; returns the public id used to join.</summary>
    private async Task<Guid> SeedConversationWithMemberAsync(Guid tenantId, Guid memberUserId)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();
        var convRepo = scope.ServiceProvider.GetRequiredService<IConversationRepository>();

        var conv = Conversation.Create(gen, ConversationType.Group, "Hub Membership Test", memberUserId, tenantId);
        conv.AddMember(ConversationMember.Create(conv.Id, memberUserId, MemberRole.Owner));
        await convRepo.AddAsync(conv, CancellationToken.None);

        return conv.PublicId;
    }

    /// <summary>Builds a synthetic authenticated hub context. Null ids omit the corresponding claim.</summary>
    private static HubCallerContext BuildContext(Guid? userId, Guid? tenantId)
    {
        var claims = new List<Claim>();
        if (userId is not null) claims.Add(new Claim("sub", userId.Value.ToString()));
        if (tenantId is not null) claims.Add(new Claim("tenant_id", tenantId.Value.ToString()));

        var context = Substitute.For<HubCallerContext>();
        context.User.Returns(new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")));
        context.ConnectionId.Returns(ConnectionId);
        return context;
    }

    private ChatHub BuildHub(IServiceScope scope, HubCallerContext context, IGroupManager groups) =>
        new(scope.ServiceProvider.GetRequiredService<IConversationRepository>(),
            Substitute.For<ILogger<ChatHub>>())
        {
            Context = context,
            Groups = groups,
        };

    [Fact]
    public async Task JoinConversation_AsMember_AddsConnectionToConversationGroup()
    {
        var tenantId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var conversationId = await SeedConversationWithMemberAsync(tenantId, memberId);

        await using var scope = _app.Services.CreateAsyncScope();
        var groups = Substitute.For<IGroupManager>();
        var hub = BuildHub(scope, BuildContext(memberId, tenantId), groups);

        var act = async () => await hub.JoinConversation(conversationId);

        await act.Should().NotThrowAsync();
        await groups.Received(1).AddToGroupAsync(
            ConnectionId, $"conversation:{conversationId}", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinConversation_AsNonMember_ThrowsHubExceptionAndDoesNotJoin()
    {
        var tenantId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var conversationId = await SeedConversationWithMemberAsync(tenantId, memberId);

        await using var scope = _app.Services.CreateAsyncScope();
        var groups = Substitute.For<IGroupManager>();
        // A different user in the SAME tenant who was never added to the conversation.
        var hub = BuildHub(scope, BuildContext(Guid.NewGuid(), tenantId), groups);

        var act = async () => await hub.JoinConversation(conversationId);

        await act.Should().ThrowAsync<HubException>();
        await groups.DidNotReceive().AddToGroupAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinConversation_CrossTenant_ThrowsHubException()
    {
        // The member's own userId but a DIFFERENT tenant in the claim: IsMemberAsync re-applies
        // TenantId by hand (filters are ignored), so the conversation resolves to nothing → denied.
        var conversationTenant = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var conversationId = await SeedConversationWithMemberAsync(conversationTenant, memberId);

        await using var scope = _app.Services.CreateAsyncScope();
        var groups = Substitute.For<IGroupManager>();
        var hub = BuildHub(scope, BuildContext(memberId, Guid.NewGuid()), groups);

        var act = async () => await hub.JoinConversation(conversationId);

        await act.Should().ThrowAsync<HubException>();
        await groups.DidNotReceive().AddToGroupAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinConversation_MissingClaims_ThrowsHubException()
    {
        // Covers the hub's own claim-extraction branch (userId/tenantId null) — reachable only by
        // driving the real hub, not by testing the repository in isolation.
        var tenantId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var conversationId = await SeedConversationWithMemberAsync(tenantId, memberId);

        await using var scope = _app.Services.CreateAsyncScope();
        var groups = Substitute.For<IGroupManager>();
        var hub = BuildHub(scope, BuildContext(userId: null, tenantId: null), groups);

        var act = async () => await hub.JoinConversation(conversationId);

        await act.Should().ThrowAsync<HubException>();
        await groups.DidNotReceive().AddToGroupAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
