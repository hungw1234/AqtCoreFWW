using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.MasterData.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.MasterData.Infrastructure.Persistence.Configurations;

public sealed class DictionaryItemConfiguration : EntityTypeConfigurationBase<DictionaryItem>
{
    protected override void ConfigureEntity(EntityTypeBuilder<DictionaryItem> e)
    {
        e.ToTable("DictionaryItems");
        e.Property(x => x.Code).HasMaxLength(100).IsRequired();
        e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        e.Property(x => x.NameVi).HasMaxLength(200).IsRequired();
        e.Property(x => x.Metadata).HasColumnType("nvarchar(max)");
        e.HasIndex(x => new { x.DictionaryId, x.Code }).IsUnique();
        e.HasIndex(x => new { x.DictionaryId, x.ParentId });
        e.HasOne(x => x.Parent).WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Dictionary).WithMany(x => x.Items)
            .HasForeignKey(x => x.DictionaryId).OnDelete(DeleteBehavior.Cascade);
    }
}
