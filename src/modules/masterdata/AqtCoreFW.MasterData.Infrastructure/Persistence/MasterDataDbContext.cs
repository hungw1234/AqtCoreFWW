using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.MasterData.Domain.Entities;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.MasterData.Infrastructure.Persistence;

/// <summary>EF Core DbContext for masterdata schema — Province/District/Ward/CaseType/JobTitle/AdministrativeUnit + Dictionary entities.</summary>
public class MasterDataDbContext(DbContextOptions<MasterDataDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext)
{
    protected override string SchemaName => "masterdata";

    public DbSet<Province> Provinces => Set<Province>();
    public DbSet<District> Districts => Set<District>();
    public DbSet<Ward> Wards => Set<Ward>();
    public DbSet<AdministrativeUnit> AdministrativeUnits => Set<AdministrativeUnit>();
    public DbSet<CaseType> CaseTypes => Set<CaseType>();
    public DbSet<JobTitle> JobTitles => Set<JobTitle>();

    public DbSet<Dictionary> Dictionaries => Set<Dictionary>();
    public DbSet<DictionaryItem> DictionaryItems => Set<DictionaryItem>();
    public DbSet<ExternalMapping> ExternalMappings => Set<ExternalMapping>();

    // Workflow masterdata taxonomy (phase-00) — soft-FK source of truth for state/action codes.
    public DbSet<WorkflowStateTaxonomy> WorkflowStateTaxonomies => Set<WorkflowStateTaxonomy>();
    public DbSet<WorkflowActionTaxonomy> WorkflowActionTaxonomies => Set<WorkflowActionTaxonomy>();

    // Province 63→34 legacy mapping (QD 19/2025/QD-TTg, effective 01/07/2025)
    public DbSet<ProvinceLegacyMapping> ProvinceLegacyMappings => Set<ProvinceLegacyMapping>();

    // Phase-04d: entity configurations extracted to Persistence/Configurations/*.cs
    // and auto-discovered by base ModuleDbContext.OnModelCreating via ApplyConfigurationsFromAssembly.
}
