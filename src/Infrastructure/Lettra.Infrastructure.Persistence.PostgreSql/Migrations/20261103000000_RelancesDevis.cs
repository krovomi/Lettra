using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.PostgreSql.Migrations
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuoteNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Recipient = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    Language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Body = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    AmountExclTax = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ValidUntil = table.Column<DateOnly>(type: "date", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WindowDays = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
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
