using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeodeDiscord.Database.Migrations
{
    /// <inheritdoc />
    public partial class RemoveStickyRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stickyRoles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stickyRoles",
                columns: table => new
                {
                    userId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    roleId = table.Column<ulong>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stickyRoles", x => new { x.userId, x.roleId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_stickyRoles_roleId",
                table: "stickyRoles",
                column: "roleId");

            migrationBuilder.CreateIndex(
                name: "IX_stickyRoles_userId",
                table: "stickyRoles",
                column: "userId");
        }
    }
}
