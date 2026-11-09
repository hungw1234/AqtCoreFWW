using AqtCoreFW.Ai.Domain.Enums;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Ai.Domain.Entities;

/// <summary>
/// Single message in an AI session.
/// Audit tier (Session 21): Public/Internal = full Content stored.
/// Confidential+ = only ContentHash (SHA-256) stored; Content is null.
///
/// T1 entity: Entity (long Id). SessionId: long (FK to T1 AiSession.Id internal PK).
/// </summary>
public sealed class AiChatMessage : Entity
{
    /// <summary>FK to AiSession.Id (long, T1 internal PK).</summary>
    public long SessionId { get; private set; }
    public ChatRole Role { get; private set; }

    /// <summary>Full message text — null when IsContentEncrypted=true (Confidential+).</summary>
    public string? Content { get; private set; }

    /// <summary>SHA-256 of content — always populated for audit traceability.</summary>
    public string ContentHash { get; private set; } = string.Empty;

    /// <summary>True when Classification >= Confidential; Content not stored raw.</summary>
    public bool IsContentEncrypted { get; private set; }

    // EF constructor
    private AiChatMessage() { }

    internal static AiChatMessage Create(long sessionId, ChatRole role, string content)
    {
        return new AiChatMessage
        {
            SessionId = sessionId,
            Role = role,
            Content = content,
            ContentHash = ComputeHash(content),
            IsContentEncrypted = false
        };
    }

    internal static AiChatMessage CreateConfidential(long sessionId, ChatRole role, string content)
    {
        return new AiChatMessage
        {
            SessionId = sessionId,
            Role = role,
            Content = null,
            ContentHash = ComputeHash(content),
            IsContentEncrypted = true
        };
    }

    private static string ComputeHash(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
