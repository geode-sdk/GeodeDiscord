using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeodeDiscord.Database.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "quotes",
                columns: table => new
                {
                    messageId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    channelId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    createdAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    lastEditedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    quoterId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    authorId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    replyAuthorId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    jumpUrl = table.Column<string>(type: "TEXT", nullable: true),
                    images = table.Column<string>(type: "TEXT", nullable: false),
                    extraAttachments = table.Column<int>(type: "INTEGER", nullable: false),
                    content = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quotes", x => x.messageId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_quotes_authorId",
                table: "quotes",
                column: "authorId");

            migrationBuilder.CreateIndex(
                name: "IX_quotes_name",
                table: "quotes",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "quotes");
        }
    }
}
