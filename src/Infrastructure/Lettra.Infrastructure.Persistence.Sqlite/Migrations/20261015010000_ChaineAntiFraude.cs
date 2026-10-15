using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.Sqlite.Migrations
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
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    LastSequence = table.Column<long>(type: "INTEGER", nullable: false),
                    LastHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrityChainHead", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntegrityClosings",
                columns: table => new
                {
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    PeriodKind = table.Column<int>(type: "INTEGER", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    FirstSequence = table.Column<long>(type: "INTEGER", nullable: false),
                    LastSequence = table.Column<long>(type: "INTEGER", nullable: false),
                    LastEntryHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TotalExclTax = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    TotalVat = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    TotalInclTax = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    CumulativeInclTax = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    PreviousClosingHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ClosedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    KeyId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    PublicKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Signature = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrityClosings", x => x.Number);
                });

            migrationBuilder.CreateTable(
                name: "IntegrityJournal",
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    OccurredAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    DocumentKind = table.Column<int>(type: "INTEGER", nullable: true),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SeriesCode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Number = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    IssueDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    CustomerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TotalExclTax = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    TotalVat = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    TotalInclTax = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: true),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RelatedDocumentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PreviousHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Seal = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrityJournal", x => x.Sequence);
                });

            // Ajout seul, au niveau de la base : un UPDATE ou un DELETE est refusé, même hors de Lettra.
            // (Qui a les droits d'administration peut supprimer ces déclencheurs : la chaîne et le sceau le détectent alors.)
            foreach (string table in new[] { "IntegrityJournal", "IntegrityClosings" })
            {
                migrationBuilder.Sql($"CREATE TRIGGER \"{table}_NoUpdate\" BEFORE UPDATE ON \"{table}\" BEGIN SELECT RAISE(ABORT, '{table} : ajout seul'); END;");
                migrationBuilder.Sql($"CREATE TRIGGER \"{table}_NoDelete\" BEFORE DELETE ON \"{table}\" BEGIN SELECT RAISE(ABORT, '{table} : ajout seul'); END;");
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
