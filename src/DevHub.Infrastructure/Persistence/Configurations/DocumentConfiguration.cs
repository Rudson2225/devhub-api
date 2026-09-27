using DevHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevHub.Infrastructure.Persistence.Configurations;

public class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("documents");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Title)
            .HasMaxLength(Document.MaxTitleLength)
            .IsRequired();

        builder.Property(d => d.Content)
            .HasMaxLength(100_000);

        builder.HasIndex(d => d.ProjectId);
        builder.HasIndex(d => d.AuthorId);

        builder.HasOne(d => d.Project)
            .WithMany(p => p.Documents)
            .HasForeignKey(d => d.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(d => d.Author)
            .WithMany(u => u.Documents)
            .HasForeignKey(d => d.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
