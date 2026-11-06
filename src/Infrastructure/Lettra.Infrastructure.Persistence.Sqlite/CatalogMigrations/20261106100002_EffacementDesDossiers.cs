using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.Sqlite.CatalogMigrations
{
    /// <inheritdoc />
    public partial class EffacementDesDossiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ErasedAt",
                table: "Dossiers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ErasureDueAt",
                table: "Dossiers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ErasureRequestedBy",
                table: "Dossiers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Dossiers_State_ErasureDueAt",
                table: "Dossiers",
                columns: new[] { "State", "ErasureDueAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Dossiers_State_ErasureDueAt",
                table: "Dossiers");

            migrationBuilder.DropColumn(
                name: "ErasedAt",
                table: "Dossiers");

            migrationBuilder.DropColumn(
                name: "ErasureDueAt",
                table: "Dossiers");

            migrationBuilder.DropColumn(
                name: "ErasureRequestedBy",
                table: "Dossiers");
        }
    }
}
