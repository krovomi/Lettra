using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.Sqlite.Migrations
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
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "QuoteFollowUps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuoteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuoteNumber = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Channel = table.Column<int>(type: "INTEGER", nullable: false),
                    Outcome = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Recipient = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    Language = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    AmountExclTax = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ValidUntil = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    OccurredOn = table.Column<DateOnly>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteFollowUps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QuoteFollowUpSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WindowDays = table.Column<int>(type: "INTEGER", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: true)
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
