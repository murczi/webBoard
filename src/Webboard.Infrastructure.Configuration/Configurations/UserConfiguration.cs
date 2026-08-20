namespace Webboard.Infrastructure.Configuration.Configurations;

using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class UserConfiguration : IEntityTypeConfiguration<UserEntity> {
    public void Configure(EntityTypeBuilder<UserEntity> builder) {
        builder.ToTable("Users");

        builder.HasKey(keyExpression: user => user.Id);

        builder.Property(propertyExpression: user => user.Name)
               .HasMaxLength(maxLength: 64);

        builder.Property(propertyExpression: user => user.PasswordHash)
               .HasMaxLength(maxLength: 255);

        builder.HasIndex(indexExpression: user => user.Name)
               .IsUnique();
    }
}
