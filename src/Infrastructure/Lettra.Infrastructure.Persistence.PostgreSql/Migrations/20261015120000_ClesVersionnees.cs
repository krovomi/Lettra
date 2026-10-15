using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class ClesVersionnees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "KeyId",
                table: "IntegrityJournal",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IntegrityKeys",
                columns: table => new
                {
                    Version = table.Column<int>(type: "integer", nullable: false),
                    KeyId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PublicKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    WrappedSecret = table.Column<byte[]>(type: "bytea", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrityKeys", x => x.Version);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrityKeys_KeyId",
                table: "IntegrityKeys",
                column: "KeyId",
                unique: true);

            // Registre des clés en ajout seul : ni suppression, ni changement de version, d'identifiant, de clé publique
            // ou de date. Seule l'enveloppe peut être réécrite (ré-enveloppement quand la KEK du cabinet tourne).
            migrationBuilder.Sql("""
                CREATE FUNCTION lettra_integrity_keys_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP <> 'UPDATE'
                        OR NEW."Version" IS DISTINCT FROM OLD."Version"
                        OR NEW."KeyId" IS DISTINCT FROM OLD."KeyId"
                        OR NEW."PublicKey" IS DISTINCT FROM OLD."PublicKey"
                        OR NEW."CreatedAt" IS DISTINCT FROM OLD."CreatedAt" THEN
                        RAISE EXCEPTION '% : ajout seul', TG_TABLE_NAME;
                    END IF;
                    RETURN NEW;
                END $$;
                """);
            migrationBuilder.Sql("""CREATE TRIGGER "IntegrityKeys_AppendOnly" BEFORE UPDATE OR DELETE ON "IntegrityKeys" FOR EACH ROW EXECUTE FUNCTION lettra_integrity_keys_guard();""");
            migrationBuilder.Sql("""CREATE TRIGGER "IntegrityKeys_NoTruncate" BEFORE TRUNCATE ON "IntegrityKeys" FOR EACH STATEMENT EXECUTE FUNCTION lettra_integrity_keys_guard();""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS lettra_integrity_keys_guard() CASCADE;");

            migrationBuilder.DropTable(
                name: "IntegrityKeys");

            migrationBuilder.DropColumn(
                name: "KeyId",
                table: "IntegrityJournal");
        }
    }
}
