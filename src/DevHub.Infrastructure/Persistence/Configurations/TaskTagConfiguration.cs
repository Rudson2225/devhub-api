using DevHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevHub.Infrastructure.Persistence.Configurations;

public class TaskTagConfiguration : IEntityTypeConfiguration<TaskTag>
{
    public void Configure(EntityTypeBuilder<TaskTag> builder)
    {
        builder.ToTable("task_tags");

        builder.HasKey(tt => new { tt.TaskId, tt.TagId });

        builder.HasOne(tt => tt.Task)
            .WithMany(t => t.Tags)
            .HasForeignKey(tt => tt.TaskId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(tt => tt.Tag)
            .WithMany(t => t.Tasks)
            .HasForeignKey(tt => tt.TagId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(tt => tt.TagId);
    }
}
