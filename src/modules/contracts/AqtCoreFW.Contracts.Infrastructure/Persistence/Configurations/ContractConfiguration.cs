using AqtCoreFW.Contracts.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Contracts.Infrastructure.Persistence.Configurations;

public sealed class ContractConfiguration : EntityTypeConfigurationBase<Contract>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Contract> builder)
    {
        builder.ToTable("Contracts");
        builder.Property(contract => contract.TenantId).IsRequired();
        builder.Property(contract => contract.ContractNumber).HasMaxLength(50).IsRequired();
        builder.Property(contract => contract.Title).HasMaxLength(200).IsRequired();
        builder.Property(contract => contract.CounterpartyName).HasMaxLength(200).IsRequired();
        builder.Property(contract => contract.StartDate).HasColumnType("date").IsRequired();
        builder.Property(contract => contract.EndDate).HasColumnType("date").IsRequired();
        builder.Property(contract => contract.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(contract => contract.Description).HasMaxLength(2000);
        builder.HasIndex(contract => new { contract.TenantId, contract.ContractNumber })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
    }
}
