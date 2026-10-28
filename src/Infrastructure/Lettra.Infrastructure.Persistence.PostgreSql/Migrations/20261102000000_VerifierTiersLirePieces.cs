using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.PostgreSql.Migrations
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
                type: "numeric(18,2)",
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
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FiguresVatAmount",
                table: "BankLineReceipts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ThirdPartyChecks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    CheckedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CheckedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnersFound = table.Column<int>(type: "integer", nullable: true),
                    Freeze = table.Column<int>(type: "integer", nullable: false),
                    FreezeMatch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Notice = table.Column<int>(type: "integer", nullable: true),
                    NoticeOn = table.Column<DateOnly>(type: "date", nullable: true),
                    NoticeLabel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Urssaf = table.Column<int>(type: "integer", nullable: true),
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
