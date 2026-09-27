using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRefreshTokenRevokedReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rows revoked before this migration kept the reason in RevokedByIp, because the entity had
            // nowhere else to put it. That column is a character varying(45) meant for an address, so
            // the values that are plainly not addresses are cleared instead of migrated: they are free
            // text, not IPs. Anything still holding a value that does look like one is left alone. In
            // either case RevokedAt is set, so the session stays revoked.
            migrationBuilder.Sql(@"UPDATE ""refresh_tokens"" SET ""RevokedByIp"" = NULL WHERE ""RevokedByIp"" IS NOT NULL AND ""RevokedByIp"" !~ '^[0-9a-fA-F:.]{2,45}$';");

            // IX_refresh_tokens_UserId is dropped because ix_refresh_tokens_user_revoked has UserId as
            // its leading column and therefore still serves the UserId foreign key.
            migrationBuilder.DropIndex(
                name: "IX_refresh_tokens_UserId",
                table: "refresh_tokens");

            migrationBuilder.AddColumn<string>(
                name: "RevokedReason",
                table: "refresh_tokens",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_revoked",
                table: "refresh_tokens",
                columns: new[] { "UserId", "RevokedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_refresh_tokens_user_revoked",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "RevokedReason",
                table: "refresh_tokens");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_UserId",
                table: "refresh_tokens",
                column: "UserId");
        }
    }
}
