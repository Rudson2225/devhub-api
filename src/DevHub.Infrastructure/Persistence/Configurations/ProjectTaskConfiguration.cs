using DevHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevHub.Infrastructure.Persistence.Configurations;

public class ProjectTaskConfiguration : IEntityTypeConfiguration<ProjectTask>
{
    public void Configure(EntityTypeBuilder<ProjectTask> builder)
    {
        builder.ToTable("tasks");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Title)
            .HasMaxLength(ProjectTask.MaxTitleLength)
            .IsRequired();

        builder.Property(t => t.Description)
            .HasMaxLength(ProjectTask.MaxDescriptionLength);

        builder.HasIndex(t => t.ProjectId);
        builder.HasIndex(t => t.AssigneeId);
        builder.HasIndex(t => t.ReporterId);
        builder.HasIndex(t => t.Status);
        builder.HasIndex(t => t.Priority);

        builder.HasOne(t => t.Project)
            .WithMany(p => p.Tasks)
            .HasForeignKey(t => t.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Assignee)
            .WithMany(u => u.AssignedTasks)
            .HasForeignKey(t => t.AssigneeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Reporter)
            .WithMany(u => u.CreatedTasks)
            .HasForeignKey(t => t.ReporterId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
