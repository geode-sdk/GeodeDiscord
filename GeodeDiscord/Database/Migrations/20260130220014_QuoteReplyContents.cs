using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeodeDiscord.Database.Migrations
{
    /// <inheritdoc />
    public partial class QuoteReplyContents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "replyContent",
                table: "quotes",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<ulong>(
                name: "replyMessageId",
                table: "quotes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0ul);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "replyContent",
                table: "quotes");

            migrationBuilder.DropColumn(
                name: "replyMessageId",
                table: "quotes");
        }
    }
}
