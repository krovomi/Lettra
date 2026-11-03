using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class RelancesDevis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "RespondedOn",
                table: "Quotes",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "QuoteFollowUps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    ErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Recipient = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    Language = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    AmountExclTax = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ValidUntil = table.Column<DateOnly>(type: "date", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    OccurredOn = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteFollowUps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QuoteFollowUpSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WindowDays = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteFollowUpSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteFollowUps_CustomerId",
                table: "QuoteFollowUps",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteFollowUps_OccurredOn",
                table: "QuoteFollowUps",
                column: "OccurredOn");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteFollowUps_QuoteId_OccurredOn",
                table: "QuoteFollowUps",
                columns: new[] { "QuoteId", "OccurredOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuoteFollowUps");

            migrationBuilder.DropTable(
                name: "QuoteFollowUpSettings");

            migrationBuilder.DropColumn(
                name: "RespondedOn",
                table: "Quotes");
        }
    }
}
