using AqtCoreFW.MasterData.Domain.Entities;

namespace AqtCoreFW.MasterData.Application.Services;

/// <summary>
/// O(1) successor resolution and region grouping for VN administrative-unit reform. The implementation
/// is a Singleton in-memory cache (built at startup); this Application abstraction lets the Presentation
/// layer read it without referencing MasterData.Infrastructure.
/// </summary>
public interface IAdministrativeUnitService
{
    /// <summary>Resolve old code → current active code (O(1) dict lookup).</summary>
    string ResolveActive(string code);

    /// <summary>Get all units by level (post-2025 reorg: 1=Province/City, 2=Ward/commune;
    /// legacy 3=Ward on un-reset data), optionally filtered by parent.</summary>
    IReadOnlyList<AdministrativeUnit> GetByLevel(int level, string? parentCode = null);

    /// <summary>Get all historical + current codes for a province (for cross-reform data queries).</summary>
    IReadOnlySet<string> GetRegionCodes(string provinceCode);
}
