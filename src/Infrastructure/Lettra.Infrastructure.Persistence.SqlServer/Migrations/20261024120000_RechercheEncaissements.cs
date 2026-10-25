using Lettra.Infrastructure.Persistence.Search;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.SqlServer.Migrations
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
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: false,
                defaultValue: "");

            // Lignes existantes : forme de recherche approchée en SQL (alphabet français) ; voir SearchKeyBackfill.
            migrationBuilder.Sql(SearchKeyBackfill.Sql("Payments", "CustomerName", sqlServer: true));
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
