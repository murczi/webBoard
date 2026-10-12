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

        builder.Property(propertyExpression: type => type.Name)
               .HasMaxLength(maxLength: 64);

        builder.HasData(Webboard.Domain.Model.Modules.ModuleTypes.All.Select(type => new ModuleTypeEntity {
            Id = type.Id, Name = type.Name, Description = type.Description
        }));
    }
}
