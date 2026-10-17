using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.SqlServer.CatalogMigrations
{
    /// <inheritdoc />
    public partial class EspaceAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                schema: "catalog",
                table: "UserAccounts",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                schema: "catalog",
                table: "UserAccounts",
                type: "nvarchar(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "catalog",
                table: "CustomRoles",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                schema: "catalog",
                table: "CustomRoles",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisplayName",
                schema: "catalog",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "Email",
                schema: "catalog",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "Description",
                schema: "catalog",
                table: "CustomRoles");

            migrationBuilder.DropColumn(
                name: "Name",
                schema: "catalog",
                table: "CustomRoles");
        }
    }
}
