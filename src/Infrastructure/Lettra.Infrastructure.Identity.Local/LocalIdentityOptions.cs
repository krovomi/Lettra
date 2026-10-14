namespace Lettra.Infrastructure.Identity.Local;

public sealed class LocalIdentityOptions
{
    public const string SectionName = "Lettra:LocalIdentity";

    /// <summary>Fichier du compte local. L'hôte MAUI le place dans le dossier de données de l'application.</summary>
    public string StorePath { get; set; } = "lettra-compte-local.json";

    /// <summary>Le poste isolé forme à lui seul un cabinet : tous ses dossiers y sont rattachés.</summary>
    public Guid CabinetId { get; set; } = new("00000000-0000-7000-8000-00000000ca01");

    /// <summary>Session fermée après cette durée sans activité (annexe F §2 : 30 minutes).</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Échecs consécutifs avant verrouillage temporaire.</summary>
    public int MaxFailedAttempts { get; set; } = 5;

    /// <summary>Durée du premier verrouillage ; elle double à chaque nouveau verrouillage (plafond 24 h).</summary>
    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Itérations PBKDF2-SHA512 (OWASP 2023 : 210 000 au minimum).</summary>
    public int Pbkdf2Iterations { get; set; } = 600_000;
}
