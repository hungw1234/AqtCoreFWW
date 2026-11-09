using AqtCoreFW.Collaboration.Domain.Entities;
using AqtCoreFW.Collaboration.Domain.Enums;
using AqtCoreFW.Collaboration.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Collaboration.Infrastructure.Clients;

/// <summary>
/// In-process implementation of IChatModuleClient.
/// Used by other modules to create contextual conversations (e.g., case discussions).
/// Zero overhead — direct repository call, no HTTP or gRPC.
/// Returns PublicId (Guid T2 AK) — callers never receive internal long Id.
/// </summary>
public sealed class InProcessChatModuleClient(
    IConversationRepository repository,
    IDomainEventPublisher eventPublisher,
    IGuidGenerator guidGen) : IChatModuleClient
{
    public async Task<Guid> CreateContextualConversationAsync(
        Guid contextEntityId,
        string contextType,
        string name,
        Guid createdBy,
        Guid tenantId,
        CancellationToken ct = default)
    {
        // T2 factory: inject IGuidGenerator for PublicId AK
        var conversation = Conversation.Create(
            guidGen,
            ConversationType.Group,
            $"{contextType}:{contextEntityId} — {name}",
            createdBy,
            tenantId);

        await repository.AddAsync(conversation, ct);

        await eventPublisher.PublishAsync(conversation.DomainEvents, ct);
        conversation.ClearDomainEvents();

        // Return PublicId (T2 Guid AK) — cross-module callers use Guid, not internal long
        return conversation.PublicId;
    }
}
