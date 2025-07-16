using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeodeDiscord.Database.Migrations
{
    /// <inheritdoc />
    public partial class StickyRolesIdFix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_stickyRoles",
                table: "stickyRoles");

            migrationBuilder.DropIndex(
                name: "IX_stickyRoles_roleId",
                table: "stickyRoles");

            migrationBuilder.AddPrimaryKey(
                name: "PK_stickyRoles",
                table: "stickyRoles",
                columns: new[] { "userId", "roleId" });

            migrationBuilder.CreateIndex(
                name: "IX_stickyRoles_roleId",
                table: "stickyRoles",
                column: "roleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_stickyRoles",
                table: "stickyRoles");

            migrationBuilder.DropIndex(
                name: "IX_stickyRoles_roleId",
                table: "stickyRoles");

            migrationBuilder.AddPrimaryKey(
                name: "PK_stickyRoles",
                table: "stickyRoles",
                column: "roleId");

            migrationBuilder.CreateIndex(
                name: "IX_stickyRoles_roleId",
                table: "stickyRoles",
                column: "roleId",
                unique: true);
        }
    }
}
