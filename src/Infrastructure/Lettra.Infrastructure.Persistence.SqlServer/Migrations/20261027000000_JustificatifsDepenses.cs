using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class JustificatifsDepenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReceiptRequestedAt",
                table: "BankStatementLines",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptRequestedTo",
                table: "BankStatementLines",
                type: "nvarchar(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReceiptRequests",
                table: "BankStatementLines",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReceiptStatus",
                table: "BankStatementLines",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "BankLineReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    Size = table.Column<int>(type: "int", nullable: false),
                    AddedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankLineReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankLineReceipts_BankStatementLines_LineId",
                        column: x => x.LineId,
                        principalTable: "BankStatementLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankLineReceipts_LineId",
                table: "BankLineReceipts",
                column: "LineId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankLineReceipts");

            migrationBuilder.DropColumn(
                name: "ReceiptRequestedAt",
                table: "BankStatementLines");

            migrationBuilder.DropColumn(
                name: "ReceiptRequestedTo",
                table: "BankStatementLines");

            migrationBuilder.DropColumn(
                name: "ReceiptRequests",
                table: "BankStatementLines");

            migrationBuilder.DropColumn(
                name: "ReceiptStatus",
                table: "BankStatementLines");
        }
    }
}
