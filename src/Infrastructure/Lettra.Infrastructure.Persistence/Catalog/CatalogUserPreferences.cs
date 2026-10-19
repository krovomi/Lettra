using Microsoft.EntityFrameworkCore;

namespace Lettra.Infrastructure.Persistence.Catalog;

/// <summary>
/// Préférences d'un utilisateur : une ligne par compte, un document JSON borné par le domaine (au plus quelques dizaines
/// de kilo-octets). Des réglages et des identifiants seulement, aucune donnée de dossier. Pas de clé étrangère vers le
/// compte : une personne connectée par Keycloak peut régler son espace avant d'avoir une ligne de compte.
/// </summary>
public sealed class CatalogUserPreferences
{
    public const int MaxDocumentLength = 200_000;

    public Guid UserId { get; set; }

    public string Document { get; set; } = "{}";

    /// <summary>Avance de 1 à chaque écriture : deux appareils qui écrivent en même temps ne s'écrasent pas.</summary>
    public long Version { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    internal static void Configure(ModelBuilder modelBuilder) => modelBuilder.Entity<CatalogUserPreferences>(e =>
    {
        e.ToTable("UserPreferences");
        e.HasKey(x => x.UserId);
        e.Property(x => x.UserId).ValueGeneratedNever();
        e.Property(x => x.Document).HasMaxLength(MaxDocumentLength);
        e.Property(x => x.Version).IsConcurrencyToken();
    });
}
