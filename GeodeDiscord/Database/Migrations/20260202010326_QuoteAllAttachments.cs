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
                name: "attachments",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    url = table.Column<string>(type: "TEXT", nullable: false),
                    contentType = table.Column<string>(type: "TEXT", nullable: true),
                    fileInQuoteMessageId = table.Column<ulong>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attachments", x => x.id);
                    table.ForeignKey(
                        name: "FK_attachments_quotes_fileInQuoteMessageId",
                        column: x => x.fileInQuoteMessageId,
                        principalTable: "quotes",
                        principalColumn: "messageId");
                });

            migrationBuilder.CreateTable(
                name: "AttachmentQuote",
                columns: table => new
                {
                    embedInQuoteMessageId = table.Column<int>(type: "INTEGER", nullable: false),
                    embedInmessageId = table.Column<ulong>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttachmentQuote", x => new { x.embedInQuoteMessageId, x.embedInmessageId });
                    table.ForeignKey(
                        name: "FK_AttachmentQuote_attachments_embedInQuoteMessageId",
                        column: x => x.embedInQuoteMessageId,
                        principalTable: "attachments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AttachmentQuote_quotes_embedInmessageId",
                        column: x => x.embedInmessageId,
                        principalTable: "quotes",
                        principalColumn: "messageId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentQuote_embedInmessageId",
                table: "AttachmentQuote",
                column: "embedInmessageId");

            migrationBuilder.CreateIndex(
                name: "IX_attachments_fileInQuoteMessageId",
                table: "attachments",
                column: "fileInQuoteMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_attachments_url",
                table: "attachments",
                column: "url",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttachmentQuote");

            migrationBuilder.DropTable(
                name: "attachments");

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
