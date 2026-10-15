using Lettra.Infrastructure.Persistence.Search;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class RechercheSansAccents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SearchKey",
                table: "Suppliers",
                type: "TEXT",
                maxLength: 400,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SearchKey",
                table: "Customers",
                type: "TEXT",
                maxLength: 400,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SearchKey",
                table: "CatalogItems",
                type: "TEXT",
                maxLength: 400,
                nullable: false,
                defaultValue: "");

            // Lignes existantes : forme de recherche approchée en SQL (alphabet français) ; voir SearchKeyBackfill.
            migrationBuilder.Sql(SearchKeyBackfill.Sql("Suppliers", "Name"));
            migrationBuilder.Sql(SearchKeyBackfill.Sql("Customers", "Name"));
            migrationBuilder.Sql(SearchKeyBackfill.Sql("CatalogItems", "Designation"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SearchKey",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "SearchKey",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "SearchKey",
                table: "CatalogItems");
        }
    }
}
