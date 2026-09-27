using DevHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevHub.Infrastructure.Persistence.Configurations;

public class ActivityLogConfiguration : IEntityTypeConfiguration<ActivityLog>
{
    public void Configure(EntityTypeBuilder<ActivityLog> builder)
    {
        builder.ToTable("activity_logs");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Message)
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(a => a.Data)
            .HasColumnType("jsonb");

        builder.HasIndex(a => a.ProjectId);
        builder.HasIndex(a => a.UserId);
        builder.HasIndex(a => a.CreatedAt);

        builder.HasOne(a => a.Project)
            .WithMany(p => p.Activities)
            .HasForeignKey(a => a.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.User)
            .WithMany(u => u.Activities)
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
