using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class BeneficiairesVerifies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccountHolderName",
                table: "Suppliers",
                type: "character varying(70)",
                maxLength: 70,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PayeeCheckResult",
                table: "Suppliers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayeeCheckSuggestedName",
                table: "Suppliers",
                type: "character varying(70)",
                maxLength: 70,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PayeeCheckedAt",
                table: "Suppliers",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccountHolderName",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "PayeeCheckResult",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "PayeeCheckSuggestedName",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "PayeeCheckedAt",
                table: "Suppliers");
        }
    }
}
