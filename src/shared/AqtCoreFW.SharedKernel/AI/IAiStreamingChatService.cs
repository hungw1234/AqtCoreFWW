namespace AqtCoreFW.SharedKernel.AI;

/// <summary>SSE streaming chat contract — base for local/cloud routing.</summary>
public interface IAiStreamingChatService
{
    IAsyncEnumerable<StreamingChatToken> StreamAsync(
        AiContext ctx,
        string userMessage,
        CancellationToken ct = default);
}
