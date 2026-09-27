using DevHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevHub.Infrastructure.Persistence.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    /// <summary>Longest revocation reason the application records ("refresh token reuse detected").</summary>
    private const int MaxRevokedReasonLength = 100;

    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.TokenHash)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(t => t.ReplacedByTokenHash)
            .HasMaxLength(128);

        builder.Property(t => t.CreatedByIp)
            .HasMaxLength(45);

        builder.Property(t => t.RevokedByIp)
            .HasMaxLength(45);

        builder.Property(t => t.RevokedReason)
            .HasMaxLength(MaxRevokedReasonLength);

        builder.HasIndex(t => t.TokenHash)
            .IsUnique();

        // The revocation sweep - run on reuse detection and on every password change - always filters
        // on UserId and RevokedAt together, so the composite index covers it. Its leading column also
        // serves the UserId foreign key, which is why there is no separate single column index.
        builder.HasIndex(t => new { t.UserId, t.RevokedAt })
            .HasDatabaseName("ix_refresh_tokens_user_revoked");

        builder.HasOne(t => t.User)
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
