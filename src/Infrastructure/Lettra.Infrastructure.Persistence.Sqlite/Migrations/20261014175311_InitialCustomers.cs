using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class InitialCustomers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Siren = table.Column<string>(type: "TEXT", maxLength: 9, nullable: true),
                    Siret = table.Column<string>(type: "TEXT", maxLength: 14, nullable: true),
                    VatNumber = table.Column<string>(type: "TEXT", maxLength: 15, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 254, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    BillingCity = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    BillingCountry = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false),
                    BillingLine1 = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    BillingLine2 = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    BillingPostalCode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_Name",
                table: "Customers",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_Siren",
                table: "Customers",
                column: "Siren");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Customers");
        }
    }
}
