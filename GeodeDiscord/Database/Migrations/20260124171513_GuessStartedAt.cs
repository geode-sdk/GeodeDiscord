using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeodeDiscord.Database.Migrations
{
    /// <inheritdoc />
    public partial class GuessStartedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "startedAt",
                table: "guesses",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "startedAt",
                table: "guesses");
        }
    }
}
