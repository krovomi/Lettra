using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.PostgreSql.CatalogMigrations
{
    /// <inheritdoc />
    public partial class DepartDesCabinets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DepartedAt",
                schema: "catalog",
                table: "Organisations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DepartureDueAt",
                schema: "catalog",
                table: "Organisations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DepartureRequestedBy",
                schema: "catalog",
                table: "Organisations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ManagedIdentity",
                schema: "catalog",
                table: "Organisations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "State",
                schema: "catalog",
                table: "Organisations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Organisations_State_DepartureDueAt",
                schema: "catalog",
                table: "Organisations",
                columns: new[] { "State", "DepartureDueAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Organisations_State_DepartureDueAt",
                schema: "catalog",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "DepartedAt",
                schema: "catalog",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "DepartureDueAt",
                schema: "catalog",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "DepartureRequestedBy",
                schema: "catalog",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "ManagedIdentity",
                schema: "catalog",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "State",
                schema: "catalog",
                table: "Organisations");
        }
    }
}
