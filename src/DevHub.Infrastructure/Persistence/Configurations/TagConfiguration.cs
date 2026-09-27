using DevHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevHub.Infrastructure.Persistence.Configurations;

public class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("tags");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name)
            .HasMaxLength(Tag.MaxNameLength)
            .IsRequired();

        builder.Property(t => t.Description)
            .HasMaxLength(500);

        builder.Property(t => t.Color)
            .HasMaxLength(20);

        // Uniqueness is scoped, not global. A global tag (ProjectId IS NULL) and a project-scoped tag
        // with the same name are distinct rows that are allowed to coexist, and two projects may each
        // own a tag with the same name. Only duplicates inside a single scope are rejected: the
        // global scope, and each project scope independently.
        builder.HasIndex(t => t.Name)
            .IsUnique()
            .HasFilter("\"ProjectId\" IS NULL")
            .HasDatabaseName("ux_tags_global_name");

        builder.HasIndex(t => new { t.ProjectId, t.Name })
            .IsUnique()
            .HasFilter("\"ProjectId\" IS NOT NULL")
            .HasDatabaseName("ux_tags_project_name");

        builder.HasOne(t => t.Project)
            .WithMany()
            .HasForeignKey(t => t.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
