using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.SqlServer.CatalogMigrations
{
    /// <inheritdoc />
    public partial class EffacementDesDossiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ErasedAt",
                schema: "catalog",
                table: "Dossiers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ErasureDueAt",
                schema: "catalog",
                table: "Dossiers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ErasureRequestedBy",
                schema: "catalog",
                table: "Dossiers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Dossiers_State_ErasureDueAt",
                schema: "catalog",
                table: "Dossiers",
                columns: new[] { "State", "ErasureDueAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Dossiers_State_ErasureDueAt",
                schema: "catalog",
                table: "Dossiers");

            migrationBuilder.DropColumn(
                name: "ErasedAt",
                schema: "catalog",
                table: "Dossiers");

            migrationBuilder.DropColumn(
                name: "ErasureDueAt",
                schema: "catalog",
                table: "Dossiers");

            migrationBuilder.DropColumn(
                name: "ErasureRequestedBy",
                schema: "catalog",
                table: "Dossiers");
        }
    }
}
