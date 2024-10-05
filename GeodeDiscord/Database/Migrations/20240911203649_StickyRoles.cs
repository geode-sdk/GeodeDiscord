using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeodeDiscord.Database.Migrations
{
    /// <inheritdoc />
    public partial class StickyRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stickyRoles",
                columns: table => new
                {
                    roleId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    userId = table.Column<ulong>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stickyRoles", x => x.roleId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stickyRoles_roleId",
                table: "stickyRoles",
                column: "roleId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stickyRoles_userId",
                table: "stickyRoles",
                column: "userId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stickyRoles");
        }
    }
}
