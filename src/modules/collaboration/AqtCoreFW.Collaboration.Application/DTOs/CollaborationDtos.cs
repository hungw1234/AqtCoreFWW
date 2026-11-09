namespace AqtCoreFW.Collaboration.Application.DTOs;

/// <summary>Conversation summary — returned by GetConversations and CreateConversation.</summary>
public sealed record ConversationDto(
    Guid Id,
    string Type,
    string? Name,
    Guid CreatedBy,
    DateTime? LastMessageAt,
    bool IsArchived,
    int MemberCount,
    string? LastMessagePreview = null,
    string? LastMessageSenderName = null,
    int UnreadCount = 0,
    IReadOnlyList<string>? ParticipantNames = null);

/// <summary>Message detail — returned by GetMessages, SendMessage, SearchMessages.</summary>
public sealed record MessageDto(
    Guid Id,
    Guid ConversationId,
    Guid SenderId,
    string? SenderName,
    string Content,
    string ContentType,
    Guid? ReplyToId,
    DateTimeOffset CreatedAt,
    DateTime? EditedAt,
    bool IsDeleted,
    bool IsSystemMessage);

/// <summary>Thread detail — reply count and last reply timestamp for a parent message.</summary>
public sealed record ThreadDto(
    Guid ParentMessageId,
    Guid ConversationId,
    int ReplyCount,
    DateTime? LastReplyAt);

/// <summary>Unread message count per conversation for a user.</summary>
public sealed record UnreadCountDto(
    Guid ConversationId,
    int UnreadCount);

/// <summary>A single read-receipt — who read a message and when. Returned by GetMessageReadReceipts.</summary>
public sealed record MessageReadReceiptDto(
    Guid UserId,
    string? FullName,
    DateTime ReadAt);
