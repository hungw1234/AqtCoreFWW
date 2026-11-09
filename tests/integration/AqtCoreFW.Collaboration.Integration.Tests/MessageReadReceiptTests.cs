using System.Net;
using System.Text.Json;
using AqtCoreFW.Collaboration.Application.Commands.MarkAsRead;
using AqtCoreFW.Collaboration.Domain.Entities;
using AqtCoreFW.Collaboration.Domain.Enums;
using AqtCoreFW.Collaboration.Domain.Repositories;
using AqtCoreFW.Collaboration.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AqtCoreFW.Collaboration.Integration.Tests;

/// <summary>
/// End-to-end coverage for the per-message read-receipt feature: MarkAsRead persists exactly one
/// receipt (idempotent on re-mark), and the GET read-receipts endpoint is membership-gated.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class MessageReadReceiptTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public MessageReadReceiptTests(WebAppFixture app) => _app = app;

    /// <summary>Seeds a conversation (owner=member) + one message; returns ids needed by the tests.</summary>
    private async Task<(Guid conversationPublicId, Guid messagePublicId, long messageInternalId)> SeedAsync(
        Guid tenantId, Guid memberUserId)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();
        var convRepo = scope.ServiceProvider.GetRequiredService<IConversationRepository>();
        var msgRepo = scope.ServiceProvider.GetRequiredService<IMessageRepository>();

        var conv = Conversation.Create(gen, ConversationType.Group, "Receipts Test", memberUserId, tenantId);
        conv.AddMember(ConversationMember.Create(conv.Id, memberUserId, MemberRole.Owner));
        await convRepo.AddAsync(conv, CancellationToken.None);

        var msg = Message.Create(gen, conv.Id, memberUserId, "hello", ContentType.Text, tenantId);
        await msgRepo.AddAsync(msg, CancellationToken.None);

        return (conv.PublicId, msg.PublicId, msg.Id);
    }

    [Fact]
    public async Task MarkAsRead_PersistsExactlyOneReceipt_AndIsIdempotent()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var (convId, msgId, msgInternalId) = await SeedAsync(tenantId, userId);

        // Mark read twice via the full MediatR pipeline.
        for (var i = 0; i < 2; i++)
        {
            await using var scope = _app.Services.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var result = await sender.Send(new MarkAsReadCommand(convId, msgId, userId), CancellationToken.None);
            result.IsSuccess.Should().BeTrue();
        }

        // Exactly one receipt persisted despite two marks (unique (MessageId, UserId) + idempotent handler).
        await using var verify = _app.Services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<CollaborationDbContext>();
        var count = await db.MessageReadStates.CountAsync(r => r.MessageId == msgInternalId && r.UserId == userId);
        count.Should().Be(1);
    }

    [Fact]
    public async Task GetReadReceipts_AsMember_Returns200WithReader()
    {
        var tenantId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var (_, msgId, _) = await SeedAsync(tenantId, memberId);

        // The member reads the message (creates the receipt) via the MarkAsRead pipeline.
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            // Resolve conversation public id for the command.
            var db = scope.ServiceProvider.GetRequiredService<CollaborationDbContext>();
            var convPublicId = await db.Messages
                .Where(m => m.PublicId == msgId)
                .Join(db.Conversations, m => m.ConversationId, c => c.Id, (m, c) => c.PublicId)
                .FirstAsync();
            (await sender.Send(new MarkAsReadCommand(convPublicId, msgId, memberId), CancellationToken.None))
                .IsSuccess.Should().BeTrue();
        }

        using var memberClient = _app.CreateAuthenticatedClient(memberId, tenantId);
        var response = await memberClient.GetAsync($"api/v1/messages/{msgId}/read-receipts");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var data = doc.RootElement.GetProperty("data");
        data.GetArrayLength().Should().BeGreaterThanOrEqualTo(1);
        data.EnumerateArray()
            .Select(e => e.GetProperty("userId").GetGuid())
            .Should().Contain(memberId);
    }

    [Fact]
    public async Task GetReadReceipts_AsNonMember_Returns403()
    {
        var tenantId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var (_, msgId, _) = await SeedAsync(tenantId, memberId);

        // A different user in the SAME tenant (so the message resolves) who is NOT a member.
        using var outsider = _app.CreateAuthenticatedClient(Guid.NewGuid(), tenantId);
        var response = await outsider.GetAsync($"api/v1/messages/{msgId}/read-receipts");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetReadReceipts_MissingMessage_Returns404()
    {
        using var client = _app.CreateAuthenticatedClient(Guid.NewGuid(), Guid.NewGuid());
        var response = await client.GetAsync($"api/v1/messages/{Guid.NewGuid()}/read-receipts");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetReadReceipts_CrossTenant_Returns404()
    {
        // Tenant isolation rests on the lookup's `m.TenantId = @TenantId` (+ RLS on Messages): a
        // requester in a different tenant must not even learn the message exists → 404, not 403.
        var tenantA = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var (_, msgId, _) = await SeedAsync(tenantA, memberId);

        using var crossTenant = _app.CreateAuthenticatedClient(Guid.NewGuid(), Guid.NewGuid());
        var response = await crossTenant.GetAsync($"api/v1/messages/{msgId}/read-receipts");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task InsertReceipt_ForNonExistentMessage_ViolatesForeignKey()
    {
        // Teeth test for the MessageId → collab.Messages FK: a receipt referencing a non-existent message
        // must be rejected by the DB (FK enforced), not silently persisted as an orphan. Without the
        // migration this insert would succeed (the column was a bare bigint).
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CollaborationDbContext>();

        const long nonExistentMessageId = long.MaxValue;
        db.MessageReadStates.Add(MessageReadState.Create(nonExistentMessageId, Guid.NewGuid()));

        var act = async () => await db.SaveChangesAsync(CancellationToken.None);

        // Pin to THIS FK by name (not just any DbUpdateException) so the test stays a genuine teeth test
        // if a future schema change adds another constraint on the same insert. The unique (MessageId,
        // UserId) index cannot fire here — both values are fresh — so the FK is the only thing that throws.
        var ex = await act.Should().ThrowAsync<DbUpdateException>();
        ex.Which.InnerException!.Message.Should().Contain("FK_MessageReadStates_Messages_MessageId");
    }

    [Fact]
    public async Task MarkAsRead_ConcurrentFirstReads_PersistExactlyOneReceipt_NoThrow()
    {
        // Exercises the idempotency contract under parallelism — the unique-index + DbUpdateException
        // benign-race branch must keep this to one row with no fault, regardless of interleaving.
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var (convId, msgId, msgInternalId) = await SeedAsync(tenantId, userId);

        async Task MarkOnce()
        {
            await using var scope = _app.Services.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            (await sender.Send(new MarkAsReadCommand(convId, msgId, userId), CancellationToken.None))
                .IsSuccess.Should().BeTrue();
        }

        var act = async () => await Task.WhenAll(MarkOnce(), MarkOnce(), MarkOnce());
        await act.Should().NotThrowAsync();

        await using var verify = _app.Services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<CollaborationDbContext>();
        var count = await db.MessageReadStates.CountAsync(r => r.MessageId == msgInternalId && r.UserId == userId);
        count.Should().Be(1);
    }
}
