using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.Sqlite.CatalogMigrations
{
    /// <inheritdoc />
    public partial class DepartDesCabinets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DepartedAt",
                table: "Organisations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DepartureDueAt",
                table: "Organisations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DepartureRequestedBy",
                table: "Organisations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ManagedIdentity",
                table: "Organisations",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "State",
                table: "Organisations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Organisations_State_DepartureDueAt",
                table: "Organisations",
                columns: new[] { "State", "DepartureDueAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Organisations_State_DepartureDueAt",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "DepartedAt",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "DepartureDueAt",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "DepartureRequestedBy",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "ManagedIdentity",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "State",
                table: "Organisations");
        }
    }
}
