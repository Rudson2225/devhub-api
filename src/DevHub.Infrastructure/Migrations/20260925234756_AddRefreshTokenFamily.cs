using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRefreshTokenFamily : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Added as nullable first so existing rows can be backfilled with a meaningful value
            // instead of collapsing into a single Guid.Empty family.
            migrationBuilder.AddColumn<Guid>(
                name: "FamilyId",
                table: "refresh_tokens",
                type: "uuid",
                nullable: true);

            // Legacy rows have no recoverable chain, so each one becomes its own family. That keeps
            // reuse detection correct: presenting a legacy token after rotation is still detected
            // as reuse through the used/revoked flags, and never merges unrelated sessions.
            migrationBuilder.Sql(@"UPDATE ""refresh_tokens"" SET ""FamilyId"" = ""Id"" WHERE ""FamilyId"" IS NULL;");

            migrationBuilder.Sql(@"ALTER TABLE ""refresh_tokens"" ALTER COLUMN ""FamilyId"" SET NOT NULL;");

            // The app always supplies FamilyId, so no database default is kept for new rows.
            migrationBuilder.Sql(@"ALTER TABLE ""refresh_tokens"" ALTER COLUMN ""FamilyId"" DROP DEFAULT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FamilyId",
                table: "refresh_tokens");
        }
    }
}
