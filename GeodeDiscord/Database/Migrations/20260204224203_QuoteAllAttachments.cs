using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeodeDiscord.Database.Migrations
{
    /// <inheritdoc />
    public partial class QuoteAllAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "extraAttachments",
                table: "quotes");

            migrationBuilder.DropColumn(
                name: "images",
                table: "quotes");

            migrationBuilder.DropColumn(
                name: "videos",
                table: "quotes");

            migrationBuilder.CreateTable(
                name: "Attachment",
                columns: table => new
                {
                    id = table.Column<ulong>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    size = table.Column<int>(type: "INTEGER", nullable: false),
                    url = table.Column<string>(type: "TEXT", nullable: false),
                    contentType = table.Column<string>(type: "TEXT", nullable: true),
                    description = table.Column<string>(type: "TEXT", nullable: true),
                    isSpoiler = table.Column<bool>(type: "INTEGER", nullable: false),
                    QuotemessageId = table.Column<ulong>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attachment", x => x.id);
                    table.ForeignKey(
                        name: "FK_Attachment_quotes_QuotemessageId",
                        column: x => x.QuotemessageId,
                        principalTable: "quotes",
                        principalColumn: "messageId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Embed",
                columns: table => new
                {
                    id = table.Column<ulong>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    type = table.Column<string>(type: "TEXT", nullable: false),
                    color = table.Column<uint>(type: "INTEGER", nullable: true),
                    providerName = table.Column<string>(type: "TEXT", nullable: true),
                    providerUrl = table.Column<string>(type: "TEXT", nullable: true),
                    authorIconUrl = table.Column<string>(type: "TEXT", nullable: true),
                    authorName = table.Column<string>(type: "TEXT", nullable: true),
                    authorUrl = table.Column<string>(type: "TEXT", nullable: true),
                    title = table.Column<string>(type: "TEXT", nullable: true),
                    url = table.Column<string>(type: "TEXT", nullable: true),
                    thumbnailUrl = table.Column<string>(type: "TEXT", nullable: true),
                    description = table.Column<string>(type: "TEXT", nullable: true),
                    videoUrl = table.Column<string>(type: "TEXT", nullable: true),
                    imageUrl = table.Column<string>(type: "TEXT", nullable: true),
                    footerIconUrl = table.Column<string>(type: "TEXT", nullable: true),
                    footerText = table.Column<string>(type: "TEXT", nullable: true),
                    timestamp = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    QuotemessageId = table.Column<ulong>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Embed", x => x.id);
                    table.ForeignKey(
                        name: "FK_Embed_quotes_QuotemessageId",
                        column: x => x.QuotemessageId,
                        principalTable: "quotes",
                        principalColumn: "messageId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Field",
                columns: table => new
                {
                    id = table.Column<ulong>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    value = table.Column<string>(type: "TEXT", nullable: false),
                    inline = table.Column<bool>(type: "INTEGER", nullable: false),
                    Embedid = table.Column<ulong>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Field", x => x.id);
                    table.ForeignKey(
                        name: "FK_Field_Embed_Embedid",
                        column: x => x.Embedid,
                        principalTable: "Embed",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Attachment_QuotemessageId",
                table: "Attachment",
                column: "QuotemessageId");

            migrationBuilder.CreateIndex(
                name: "IX_Embed_QuotemessageId",
                table: "Embed",
                column: "QuotemessageId");

            migrationBuilder.CreateIndex(
                name: "IX_Field_Embedid",
                table: "Field",
                column: "Embedid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Attachment");

            migrationBuilder.DropTable(
                name: "Field");

            migrationBuilder.DropTable(
                name: "Embed");

            migrationBuilder.AddColumn<int>(
                name: "extraAttachments",
                table: "quotes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "images",
                table: "quotes",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "videos",
                table: "quotes",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }
    }
}
