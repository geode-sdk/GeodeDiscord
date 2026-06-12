using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeodeDiscord.Database.Migrations
{
    /// <inheritdoc />
    public partial class Privacy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "contentRedacted",
                table: "quotes",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "replyRedacted",
                table: "quotes",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "optOuts",
                columns: table => new
                {
                    userId = table.Column<ulong>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_optOuts", x => x.userId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "optOuts");

            migrationBuilder.DropColumn(
                name: "contentRedacted",
                table: "quotes");

            migrationBuilder.DropColumn(
                name: "replyRedacted",
                table: "quotes");
        }
    }
}
