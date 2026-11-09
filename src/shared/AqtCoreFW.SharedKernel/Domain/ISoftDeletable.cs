namespace AqtCoreFW.SharedKernel.Domain;

/// <summary>
/// Marker interface for soft-deletable entities.
/// EF global query filters and SoftDeleteInterceptor rely on this interface.
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; }
    void MarkDeleted();
}
