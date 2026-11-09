using AqtCoreFW.Infrastructure.Persistence;

namespace AqtCoreFW.Files.Infrastructure.Persistence;

/// <summary>
/// Thin generic repository adapter for Files module entities that don't need custom queries.
/// Passes FilesDbContext (a ModuleDbContext) to GenericRepository base class.
/// Used for FileVersion, DocumentTemplate, RetentionPolicy, RecordLifecycle.
/// Phase-4b B3b: TId is long (bigint identity PK) for all T1/T3 entities in files module.
/// </summary>
public sealed class GenericFilesRepository<T>(FilesDbContext context)
    : GenericRepository<T, long>(context)
    where T : class;
