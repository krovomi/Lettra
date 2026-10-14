using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class TiersCatalogue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CatalogItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ReferenceKey = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Designation = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    UnitPriceExclTax = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    VatRate = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Suppliers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Siren = table.Column<string>(type: "TEXT", maxLength: 9, nullable: true),
                    Siret = table.Column<string>(type: "TEXT", maxLength: 14, nullable: true),
                    VatNumber = table.Column<string>(type: "TEXT", maxLength: 15, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 254, nullable: true),
                    Iban = table.Column<string>(type: "TEXT", maxLength: 34, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    City = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Country = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false),
                    AddressLine1 = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AddressLine2 = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PostalCode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suppliers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogItems_Designation",
                table: "CatalogItems",
                column: "Designation");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogItems_ReferenceKey",
                table: "CatalogItems",
                column: "ReferenceKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_Name",
                table: "Suppliers",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_Siren",
                table: "Suppliers",
                column: "Siren");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogItems");

            migrationBuilder.DropTable(
                name: "Suppliers");
        }
    }
}
