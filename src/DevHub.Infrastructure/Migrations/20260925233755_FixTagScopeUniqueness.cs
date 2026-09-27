using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixTagScopeUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Safe on existing data: the old globally-unique IX_tags_Name already guaranteed that no
            // two rows shared a Name, so neither per-scope unique index can be violated here.
            // IX_tags_ProjectId is dropped because ux_tags_project_name has ProjectId as its leading
            // column and therefore still serves the ProjectId foreign key.
            migrationBuilder.DropIndex(
                name: "IX_tags_Name",
                table: "tags");

            migrationBuilder.DropIndex(
                name: "IX_tags_ProjectId",
                table: "tags");

            migrationBuilder.CreateIndex(
                name: "ux_tags_global_name",
                table: "tags",
                column: "Name",
                unique: true,
                filter: "\"ProjectId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_tags_project_name",
                table: "tags",
                columns: new[] { "ProjectId", "Name" },
                unique: true,
                filter: "\"ProjectId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Note: recreating the globally-unique IX_tags_Name only succeeds if no two tags share a
            // name across different scopes at the time of the rollback, which is precisely the state
            // this migration removes. A rollback is therefore only expected on a database that has
            // not yet created same-named tags in two different scopes.
            migrationBuilder.DropIndex(
                name: "ux_tags_global_name",
                table: "tags");

            migrationBuilder.DropIndex(
                name: "ux_tags_project_name",
                table: "tags");

            migrationBuilder.CreateIndex(
                name: "IX_tags_Name",
                table: "tags",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tags_ProjectId",
                table: "tags",
                column: "ProjectId");
        }
    }
}
