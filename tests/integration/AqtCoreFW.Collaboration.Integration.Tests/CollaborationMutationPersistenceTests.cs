using AqtCoreFW.Collaboration.Application.Commands.DeleteMessage;
using AqtCoreFW.Collaboration.Application.Commands.MarkAsRead;
using AqtCoreFW.Collaboration.Domain.Entities;
using AqtCoreFW.Collaboration.Domain.Enums;
using AqtCoreFW.Collaboration.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AqtCoreFW.Collaboration.Integration.Tests;

/// <summary>
/// Integration test to empirically verify whether DeleteMessageCommandHandler and
/// MarkAsReadCommandHandler persist their mutations to the database.
///
/// SUSPECTED DEFECT: Both handlers call domain methods (message.SoftDelete(), member.UpdateLastRead(...))
/// but neither calls SaveChangesAsync() in the request path. This test confirms whether the
/// mutations persist or are lost.
///
/// Test strategy:
/// 1. Seed a conversation + message + member via repositories (which DO save).
/// 2. Invoke the command handlers via ISender.
/// 3. In a FRESH DI scope (new DbContext), re-query and assert the mutation persisted.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class CollaborationMutationPersistenceTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public CollaborationMutationPersistenceTests(WebAppFixture app)
    {
        _app = app;
    }

    /// <summary>
    /// DeleteMessageCommand handler should persist soft-delete to the database.
    /// If defect exists, DeletedAt will remain null even after the handler runs.
    /// </summary>
    [Fact]
    public async Task DeleteMessageCommandHandler_SoftDeletesPersistsToDB()
    {
        // ========== ARRANGE: Seed conversation + message in first DI scope ==========
        Guid conversationPublicId;
        Guid messagePublicId;
        Guid authorUserId;
        Guid tenantId = Guid.NewGuid();

        using (var scope = _app.Services.CreateAsyncScope())
        {
            var guidGenerator = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();
            var conversationRepo = scope.ServiceProvider.GetRequiredService<IConversationRepository>();
            var messageRepo = scope.ServiceProvider.GetRequiredService<IMessageRepository>();

            authorUserId = Guid.NewGuid();

            // Create conversation with the author as a member
            var conversation = Conversation.Create(
                guidGenerator, ConversationType.Direct, "Test Conversation", authorUserId, tenantId);
            var member = ConversationMember.Create(conversation.Id, authorUserId, MemberRole.Owner);
            conversation.AddMember(member);

            conversationPublicId = conversation.PublicId;
            await conversationRepo.AddAsync(conversation, CancellationToken.None);

            // Create message authored by the author
            var message = Message.Create(
                guidGenerator,
                conversation.Id,
                authorUserId,
                "Test message content",
                ContentType.Text,
                tenantId);

            messagePublicId = message.PublicId;
            await messageRepo.AddAsync(message, CancellationToken.None);
        }

        // ========== ACT: Delete message in second DI scope ==========
        using (var scope = _app.Services.CreateAsyncScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();

            var command = new DeleteMessageCommand(messagePublicId, authorUserId);
            var result = await sender.Send(command, CancellationToken.None);

            // Command should return success (no validation errors)
            result.IsSuccess.Should().BeTrue(
                because: "DeleteMessageCommand should succeed for author-owned message");
        }

        // ========== ASSERT: Verify soft-delete persisted in a FRESH scope ==========
        using (var scope = _app.Services.CreateAsyncScope())
        {
            var messageRepo = scope.ServiceProvider.GetRequiredService<IMessageRepository>();

            var requeried = await messageRepo.GetByPublicIdAsync(messagePublicId, CancellationToken.None);

            requeried.Should().NotBeNull("message must exist in DB");
            requeried!.IsMessageDeleted.Should().BeTrue(
                because: "DEFECT CHECK: DeletedAt should be set after DeleteMessageCommandHandler runs. " +
                         "If this fails, the handler is not persisting the SoftDelete() mutation to the database.");
            requeried.DeletedAt.Should().NotBeNull();
        }
    }

    /// <summary>
    /// MarkAsReadCommand handler should persist LastReadMessageId to the conversation member.
    /// If defect exists, LastReadMessageId will remain null even after the handler runs.
    /// </summary>
    [Fact]
    public async Task MarkAsReadCommandHandler_UpdatesLastReadPersistsToDB()
    {
        // ========== ARRANGE: Seed conversation + message + member in first DI scope ==========
        Guid conversationPublicId;
        Guid messagePublicId;
        Guid readerUserId;
        Guid tenantId = Guid.NewGuid();

        using (var scope = _app.Services.CreateAsyncScope())
        {
            var guidGenerator = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();
            var conversationRepo = scope.ServiceProvider.GetRequiredService<IConversationRepository>();
            var messageRepo = scope.ServiceProvider.GetRequiredService<IMessageRepository>();

            var authorUserId = Guid.NewGuid();
            readerUserId = Guid.NewGuid();

            // Create conversation with both author and reader as members
            var conversation = Conversation.Create(
                guidGenerator, ConversationType.Group, "Test Conversation", authorUserId, tenantId);

            var authorMember = ConversationMember.Create(conversation.Id, authorUserId, MemberRole.Owner);
            var readerMember = ConversationMember.Create(conversation.Id, readerUserId, MemberRole.Member);
            conversation.AddMember(authorMember);
            conversation.AddMember(readerMember);

            conversationPublicId = conversation.PublicId;
            await conversationRepo.AddAsync(conversation, CancellationToken.None);

            // Create message authored by author
            var message = Message.Create(
                guidGenerator,
                conversation.Id,
                authorUserId,
                "Test message content",
                ContentType.Text,
                tenantId);

            messagePublicId = message.PublicId;
            await messageRepo.AddAsync(message, CancellationToken.None);
        }

        // ========== ACT: Mark message as read by reader in second DI scope ==========
        using (var scope = _app.Services.CreateAsyncScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();

            var command = new MarkAsReadCommand(conversationPublicId, messagePublicId, readerUserId);
            var result = await sender.Send(command, CancellationToken.None);

            // Command should return success
            result.IsSuccess.Should().BeTrue(
                because: "MarkAsReadCommand should succeed for conversation member");
        }

        // ========== ASSERT: Verify LastReadMessageId persisted in FRESH scope ==========
        using (var scope = _app.Services.CreateAsyncScope())
        {
            var conversationRepo = scope.ServiceProvider.GetRequiredService<IConversationRepository>();

            var requeried = await conversationRepo.GetByPublicIdAsync(conversationPublicId, CancellationToken.None);

            requeried.Should().NotBeNull("conversation must exist in DB");
            var member = requeried!.Members.FirstOrDefault(m => m.UserId == readerUserId);

            member.Should().NotBeNull("reader member must be in conversation");
            member!.LastReadMessageId.Should().NotBeNull(
                because: "DEFECT CHECK: LastReadMessageId should be updated after MarkAsReadCommandHandler runs. " +
                         "If this fails, the handler is not persisting the UpdateLastRead(...) mutation to the database.");
        }
    }
}
