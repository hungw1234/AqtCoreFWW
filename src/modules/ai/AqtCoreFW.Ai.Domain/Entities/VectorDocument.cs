using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Ai.Domain.Entities;

/// <summary>
/// Metadata for documents indexed in the vector store.
/// Tracks ingestion state per tenant — does NOT store the actual vectors (those live in Qdrant).
/// Collection name follows pattern: vectors_{tenantId:N}.
///
/// T1 entity: Entity (long Id). TenantId: Guid (FK exception list — tenant isolation key).
/// SourceDocumentId: Guid? (cross-module ref to Files module document; Files module uses Guid PK — FK exception).
/// </summary>
public sealed class VectorDocument : Entity
{
    public Guid TenantId { get; private set; }

    /// <summary>Qdrant collection name — vectors_{tenantId:N}.</summary>
    public string Collection { get; private set; } = string.Empty;

    /// <summary>
    /// Cross-module ref to Files module document (nullable — supports adhoc text ingestion).
    /// Guid kept: Files module document PK is Guid (framework-managed); cannot flip to long without Phase-4b Files refactor.
    /// </summary>
    public Guid? SourceDocumentId { get; private set; }

    public string OriginalFileName { get; private set; } = string.Empty;
    public int ChunkCount { get; private set; }
    public DateTime IndexedAtUtc { get; private set; } = DateTime.UtcNow;

    // EF constructor
    private VectorDocument() { }

    public static VectorDocument Create(
        Guid tenantId,
        string collection,
        Guid? sourceDocumentId,
        string originalFileName,
        int chunkCount)
    {
        return new VectorDocument
        {
            TenantId = tenantId,
            Collection = collection,
            SourceDocumentId = sourceDocumentId,
            OriginalFileName = originalFileName,
            ChunkCount = chunkCount,
            IndexedAtUtc = DateTime.UtcNow
        };
    }

    /// <summary>Updates chunk count after re-indexing.</summary>
    public void UpdateChunkCount(int chunkCount)
    {
        ChunkCount = chunkCount;
        MarkUpdated();
    }
}
