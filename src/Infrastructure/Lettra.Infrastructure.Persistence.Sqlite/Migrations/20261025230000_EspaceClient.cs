using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class EspaceClient : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PortalLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SecretHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    QuoteId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DownPaymentPercent = table.Column<decimal>(type: "TEXT", precision: 5, scale: 2, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    RevokedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    LastOpenedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortalLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QuoteSignatures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuoteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PortalLinkId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SignerName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    SignedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DocumentHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ProviderReference = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DownPaymentInvoiceId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteSignatures", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PortalLinks_CustomerId",
                table: "PortalLinks",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_PortalLinks_SecretHash",
                table: "PortalLinks",
                column: "SecretHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuoteSignatures_QuoteId",
                table: "QuoteSignatures",
                column: "QuoteId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PortalLinks");

            migrationBuilder.DropTable(
                name: "QuoteSignatures");
        }
    }
}
