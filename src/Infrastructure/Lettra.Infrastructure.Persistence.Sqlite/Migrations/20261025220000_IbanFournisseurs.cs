using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class IbanFournisseurs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupplierIbanChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SupplierId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PreviousIban = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    NewIban = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CountryChanged = table.Column<bool>(type: "INTEGER", nullable: false),
                    ChangedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    DecidedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Method = table.Column<int>(type: "INTEGER", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierIbanChanges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierIbanChanges_Status",
                table: "SupplierIbanChanges",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierIbanChanges_SupplierId_Status",
                table: "SupplierIbanChanges",
                columns: new[] { "SupplierId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupplierIbanChanges");
        }
    }
}
