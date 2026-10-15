using Lettra.Infrastructure.Persistence.Search;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.SqlServer.Migrations
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
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SearchKey",
                table: "Customers",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SearchKey",
                table: "CatalogItems",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: false,
                defaultValue: "");

            // Lignes existantes : forme de recherche approchée en SQL (alphabet français) ; voir SearchKeyBackfill.
            migrationBuilder.Sql(SearchKeyBackfill.Sql("Suppliers", "Name", sqlServer: true));
            migrationBuilder.Sql(SearchKeyBackfill.Sql("Customers", "Name", sqlServer: true));
            migrationBuilder.Sql(SearchKeyBackfill.Sql("CatalogItems", "Designation", sqlServer: true));
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
