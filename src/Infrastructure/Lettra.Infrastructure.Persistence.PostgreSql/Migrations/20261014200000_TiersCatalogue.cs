using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.PostgreSql.Migrations
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Reference = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ReferenceKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Designation = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Unit = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    UnitPriceExclTax = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    VatRate = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Suppliers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Siren = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    Siret = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    VatNumber = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    Iban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    City = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    AddressLine1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AddressLine2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PostalCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
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
