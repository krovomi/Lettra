using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.SqlServer.Migrations
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
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IntegrityKeys",
                columns: table => new
                {
                    Version = table.Column<int>(type: "int", nullable: false),
                    KeyId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PublicKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    WrappedSecret = table.Column<byte[]>(type: "varbinary(128)", maxLength: 128, nullable: false),
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
            migrationBuilder.Sql("CREATE TRIGGER [IntegrityKeys_NoDelete] ON [IntegrityKeys] INSTEAD OF DELETE AS BEGIN THROW 51000, N'IntegrityKeys : ajout seul', 1; END");
            migrationBuilder.Sql("""
                CREATE TRIGGER [IntegrityKeys_Immutable] ON [IntegrityKeys] AFTER UPDATE AS
                BEGIN
                    IF UPDATE([Version]) OR UPDATE([KeyId]) OR UPDATE([PublicKey]) OR UPDATE([CreatedAt])
                    BEGIN;
                        THROW 51000, N'IntegrityKeys : ajout seul', 1;
                    END;
                END
                """);
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
