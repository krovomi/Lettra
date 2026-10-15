using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.Sqlite.Migrations
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
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IntegrityKeys",
                columns: table => new
                {
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    KeyId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    PublicKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    WrappedSecret = table.Column<byte[]>(type: "BLOB", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
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
                CREATE TRIGGER "IntegrityKeys_Immutable" BEFORE UPDATE OF "Version", "KeyId", "PublicKey", "CreatedAt" ON "IntegrityKeys"
                BEGIN SELECT RAISE(ABORT, 'IntegrityKeys : ajout seul'); END;
                """);
            migrationBuilder.Sql("""CREATE TRIGGER "IntegrityKeys_NoDelete" BEFORE DELETE ON "IntegrityKeys" BEGIN SELECT RAISE(ABORT, 'IntegrityKeys : ajout seul'); END;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IntegrityKeys");

            migrationBuilder.DropColumn(
                name: "KeyId",
                table: "IntegrityJournal");
        }
    }
}
