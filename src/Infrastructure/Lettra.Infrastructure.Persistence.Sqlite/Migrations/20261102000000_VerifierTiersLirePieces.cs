using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class VerifierTiersLirePieces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "FiguresAmountInclTax",
                table: "BankLineReceipts",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "FiguresDate",
                table: "BankLineReceipts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FiguresReadOnDevice",
                table: "BankLineReceipts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FiguresVatAmount",
                table: "BankLineReceipts",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ThirdPartyChecks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    CheckedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CheckedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnersFound = table.Column<int>(type: "INTEGER", nullable: true),
                    Freeze = table.Column<int>(type: "INTEGER", nullable: false),
                    FreezeMatch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Notice = table.Column<int>(type: "INTEGER", nullable: true),
                    NoticeOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    NoticeLabel = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Urssaf = table.Column<int>(type: "INTEGER", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThirdPartyChecks", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ThirdPartyChecks");

            migrationBuilder.DropColumn(
                name: "FiguresAmountInclTax",
                table: "BankLineReceipts");

            migrationBuilder.DropColumn(
                name: "FiguresDate",
                table: "BankLineReceipts");

            migrationBuilder.DropColumn(
                name: "FiguresReadOnDevice",
                table: "BankLineReceipts");

            migrationBuilder.DropColumn(
                name: "FiguresVatAmount",
                table: "BankLineReceipts");
        }
    }
}
