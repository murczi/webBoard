namespace Webboard.Infrastructure.Configuration.Configurations;

using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class ModuleTypeConfiguration : IEntityTypeConfiguration<ModuleTypeEntity> {
    public void Configure(EntityTypeBuilder<ModuleTypeEntity> builder) {
        builder.ToTable("ModuleTypes");

        builder.HasKey(keyExpression: type => type.Id);

        builder.Property(propertyExpression: type => type.Description)
               .HasMaxLength(maxLength: 128);

        builder.Property(propertyExpression: type => type.Type)
               .HasMaxLength(maxLength: 64);

        builder.HasData(
        new ModuleTypeEntity
        {
            Id = 1,
            Type = "Http",
            Description = "Http Module"
        }
        );
    }
}
