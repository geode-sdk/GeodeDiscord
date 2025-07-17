using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeodeDiscord.Database.Migrations
{
    /// <inheritdoc />
    public partial class SeparateQuoteIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_quotes_name",
                table: "quotes");

            migrationBuilder.AddColumn<int>(
                name: "id",
                table: "quotes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                UPDATE quotes
                SET id = CAST(name AS INTEGER)
                WHERE CAST(name AS INTEGER) >= 1;

                WITH negatives AS (
                    SELECT rowid, createdAt
                    FROM quotes
                    WHERE id = 0
                ),
                numbered AS (
                    SELECT rowid, ROW_NUMBER() OVER (ORDER BY createdAt DESC) AS rn
                    FROM negatives
                )
                UPDATE quotes
                SET id = -(numbered.rn)
                FROM numbered
                WHERE quotes.rowid = numbered.rowid;

                UPDATE quotes
                SET name = TRIM(LTRIM(LTRIM(LTRIM(REPLACE(name, CAST(id as TEXT), '')), '/'), ':'));
                """
            );

            migrationBuilder.CreateIndex(
                name: "IX_quotes_id",
                table: "quotes",
                column: "id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_quotes_name",
                table: "quotes",
                column: "name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_quotes_id",
                table: "quotes");

            migrationBuilder.DropIndex(
                name: "IX_quotes_name",
                table: "quotes");

            migrationBuilder.Sql(
                """
                UPDATE quotes
                SET name = id + ': ' + name
                WHERE name != '';

                UPDATE quotes
                SET name = id
                WHERE name == '';
                """
            );

            migrationBuilder.DropColumn(
                name: "id",
                table: "quotes");

            migrationBuilder.CreateIndex(
                name: "IX_quotes_name",
                table: "quotes",
                column: "name",
                unique: true);
        }
    }
}
