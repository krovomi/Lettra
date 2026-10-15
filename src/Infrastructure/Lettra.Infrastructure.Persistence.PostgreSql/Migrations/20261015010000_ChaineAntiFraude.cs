using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.PostgreSql.Migrations
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
                    Id = table.Column<int>(type: "integer", nullable: false),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false),
                    LastHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
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
                    Number = table.Column<int>(type: "integer", nullable: false),
                    PeriodKind = table.Column<int>(type: "integer", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    FirstSequence = table.Column<long>(type: "bigint", nullable: false),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false),
                    LastEntryHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TotalExclTax = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalVat = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalInclTax = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CumulativeInclTax = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    PreviousClosingHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClosedAt = table.Column<long>(type: "bigint", nullable: false),
                    KeyId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PublicKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Signature = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
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
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    DocumentKind = table.Column<int>(type: "integer", nullable: true),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    SeriesCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    TotalExclTax = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalVat = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalInclTax = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RelatedDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    PreviousHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Seal = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrityJournal", x => x.Sequence);
                });

            // Ajout seul, au niveau de la base : UPDATE, DELETE et TRUNCATE sont refusés, même hors de Lettra.
            // (Qui a les droits d'administration peut supprimer ces déclencheurs : la chaîne et le sceau le détectent alors.)
            migrationBuilder.Sql("""
                CREATE FUNCTION lettra_integrity_append_only() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION '% : ajout seul', TG_TABLE_NAME;
                END $$;
                """);
            foreach (string table in new[] { "IntegrityJournal", "IntegrityClosings" })
            {
                migrationBuilder.Sql($"CREATE TRIGGER \"{table}_AppendOnly\" BEFORE UPDATE OR DELETE ON \"{table}\" FOR EACH ROW EXECUTE FUNCTION lettra_integrity_append_only();");
                migrationBuilder.Sql($"CREATE TRIGGER \"{table}_NoTruncate\" BEFORE TRUNCATE ON \"{table}\" FOR EACH STATEMENT EXECUTE FUNCTION lettra_integrity_append_only();");
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
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS lettra_integrity_append_only() CASCADE;");

            migrationBuilder.DropTable(
                name: "IntegrityChainHead");

            migrationBuilder.DropTable(
                name: "IntegrityClosings");

            migrationBuilder.DropTable(
                name: "IntegrityJournal");
        }
    }
}
