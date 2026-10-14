namespace Lettra.Infrastructure.External;

/// <summary>Réglages des services externes (section <c>Lettra:External</c>).</summary>
public sealed class ExternalServicesOptions
{
    public const string SectionName = "Lettra:External";

    /// <summary>
    /// Faux pour couper tout appel sortant (poste sans réseau, cabinet qui refuse les annuaires) :
    /// les recherches répondent alors <c>Directory.Unavailable</c> sans tenter de connexion.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>API Recherche d'entreprises (DINUM), gratuite et sans clé.</summary>
    public Uri SireneBaseAddress { get; set; } = new("https://recherche-entreprises.api.gouv.fr/");

    /// <summary>API REST de VIES (Commission européenne).</summary>
    public Uri ViesBaseAddress { get; set; } = new("https://ec.europa.eu/taxation_customs/vies/rest-api/");

    /// <summary>Délai maximal d'un appel, nouvelle tentative comprise : l'utilisateur attend devant l'écran.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>Durée de conservation d'une réponse de l'annuaire SIRENE (données mises à jour chaque jour).</summary>
    public TimeSpan SireneCacheDuration { get; set; } = TimeSpan.FromHours(12);

    /// <summary>Durée de conservation d'une réponse VIES. Courte : la date de consultation sert de preuve.</summary>
    public TimeSpan ViesCacheDuration { get; set; } = TimeSpan.FromMinutes(30);
}
