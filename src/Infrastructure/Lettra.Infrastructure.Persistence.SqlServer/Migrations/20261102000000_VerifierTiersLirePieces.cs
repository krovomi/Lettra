using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.SqlServer.Migrations
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
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "FiguresDate",
                table: "BankLineReceipts",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FiguresReadOnDevice",
                table: "BankLineReceipts",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FiguresVatAmount",
                table: "BankLineReceipts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ThirdPartyChecks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    CheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CheckedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnersFound = table.Column<int>(type: "int", nullable: true),
                    Freeze = table.Column<int>(type: "int", nullable: false),
                    FreezeMatch = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Notice = table.Column<int>(type: "int", nullable: true),
                    NoticeOn = table.Column<DateOnly>(type: "date", nullable: true),
                    NoticeLabel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Urssaf = table.Column<int>(type: "int", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
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
