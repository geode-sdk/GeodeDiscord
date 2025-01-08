using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeodeDiscord.Database.Migrations
{
    /// <inheritdoc />
    public partial class IndexTokens : Migration
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
                column: "userId");

            migrationBuilder.CreateTable(
                name: "indexTokens",
                columns: table => new
                {
                    userId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    indexToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indexTokens", x => x.userId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stickyRoles_roleId",
                table: "stickyRoles",
                column: "roleId");

            migrationBuilder.CreateIndex(
                name: "IX_indexTokens_userId",
                table: "indexTokens",
                column: "userId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "indexTokens");

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
