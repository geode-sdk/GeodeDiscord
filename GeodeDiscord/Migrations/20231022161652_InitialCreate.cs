using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeodeDiscord.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
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
                    jumpUrl = table.Column<string>(type: "TEXT", nullable: true),
                    timestamp = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    authorName = table.Column<string>(type: "TEXT", nullable: false),
                    authorIcon = table.Column<string>(type: "TEXT", nullable: false),
                    authorId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    images = table.Column<string>(type: "TEXT", nullable: false),
                    extraAttachments = table.Column<int>(type: "INTEGER", nullable: false),
                    content = table.Column<string>(type: "TEXT", nullable: false),
                    replyAuthorId = table.Column<ulong>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quotes", x => new { x.messageId, x.name });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "quotes");
        }
    }
}
