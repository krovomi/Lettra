using Lettra.Infrastructure.Persistence.Search;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class RechercheEncaissements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SearchKey",
                table: "Payments",
                type: "TEXT",
                maxLength: 400,
                nullable: false,
                defaultValue: "");

            // Lignes existantes : forme de recherche approchée en SQL (alphabet français) ; voir SearchKeyBackfill.
            migrationBuilder.Sql(SearchKeyBackfill.Sql("Payments", "CustomerName"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SearchKey",
                table: "Payments");
        }
    }
}
