using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class ChaineAntiFraude : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IntegrityChainHead",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false),
                    LastHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrityChainHead", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntegrityClosings",
                columns: table => new
                {
                    Number = table.Column<int>(type: "int", nullable: false),
                    PeriodKind = table.Column<int>(type: "int", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    FirstSequence = table.Column<long>(type: "bigint", nullable: false),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false),
                    LastEntryHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TotalExclTax = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalVat = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalInclTax = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CumulativeInclTax = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PreviousClosingHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ClosedAt = table.Column<long>(type: "bigint", nullable: false),
                    KeyId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PublicKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Signature = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrityClosings", x => x.Number);
                });

            migrationBuilder.CreateTable(
                name: "IntegrityJournal",
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    OccurredAt = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    DocumentKind = table.Column<int>(type: "int", nullable: true),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SeriesCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Number = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TotalExclTax = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalVat = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalInclTax = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RelatedDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PreviousHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Seal = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrityJournal", x => x.Sequence);
                });

            // Ajout seul, au niveau de la base : un UPDATE ou un DELETE est refusé, même hors de Lettra.
            // (Qui a les droits d'administration peut supprimer ces déclencheurs : la chaîne et le sceau le détectent alors.)
            foreach (string table in new[] { "IntegrityJournal", "IntegrityClosings" })
            {
                migrationBuilder.Sql($"CREATE TRIGGER [{table}_AppendOnly] ON [{table}] INSTEAD OF UPDATE, DELETE AS BEGIN THROW 51000, N'{table} : ajout seul', 1; END");
            }

            migrationBuilder.InsertData(
                table: "IntegrityChainHead",
                columns: new[] { "Id", "LastHash", "LastSequence", "Version" },
                values: new object[] { 1, "", 0L, 0L });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrityJournal_DocumentId",
                table: "IntegrityJournal",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_IntegrityJournal_OccurredAt",
                table: "IntegrityJournal",
                column: "OccurredAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IntegrityChainHead");

            migrationBuilder.DropTable(
                name: "IntegrityClosings");

            migrationBuilder.DropTable(
                name: "IntegrityJournal");
        }
    }
}
