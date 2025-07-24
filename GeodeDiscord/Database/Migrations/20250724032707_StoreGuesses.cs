using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeodeDiscord.Database.Migrations
{
    /// <inheritdoc />
    public partial class StoreGuesses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "guessStats");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "quotes",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "guesses",
                columns: table => new
                {
                    messageId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    guessedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    userId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    guessId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    quoteMessageId = table.Column<ulong>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_guesses", x => x.messageId);
                    table.ForeignKey(
                        name: "FK_guesses_quotes_quoteMessageId",
                        column: x => x.quoteMessageId,
                        principalTable: "quotes",
                        principalColumn: "messageId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_guesses_guessedAt",
                table: "guesses",
                column: "guessedAt");

            migrationBuilder.CreateIndex(
                name: "IX_guesses_quoteMessageId",
                table: "guesses",
                column: "quoteMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_guesses_userId",
                table: "guesses",
                column: "userId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "guesses");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "quotes",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.CreateTable(
                name: "guessStats",
                columns: table => new
                {
                    userId = table.Column<ulong>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    correct = table.Column<ulong>(type: "INTEGER", nullable: false),
                    maxStreak = table.Column<ulong>(type: "INTEGER", nullable: false),
                    streak = table.Column<ulong>(type: "INTEGER", nullable: false),
                    total = table.Column<ulong>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_guessStats", x => x.userId);
                });
        }
    }
}
