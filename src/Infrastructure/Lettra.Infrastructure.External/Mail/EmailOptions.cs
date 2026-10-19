namespace Lettra.Infrastructure.External.Mail;

/// <summary>
/// Réglages de l'envoi d'e-mails (section <c>Lettra:Email</c>). Coupé par défaut : sans réglage, aucun message ne part
/// et les écrans proposent l'envoi à la main. Dans la démo Docker, le serveur est MailPit, qui garde les messages pour
/// les lire dans le navigateur et n'envoie rien dehors.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Lettra:Email";

    /// <summary>Vrai pour envoyer par SMTP ; faux (par défaut) pour ne rien envoyer.</summary>
    public bool Enabled { get; set; }

    /// <summary>Serveur SMTP (<c>mailpit</c> dans la démo).</summary>
    public string Host { get; set; } = "";

    /// <summary>Port SMTP : 1025 pour MailPit, 587 pour un relais avec STARTTLS.</summary>
    public int Port { get; set; } = 587;

    /// <summary>Chiffrement STARTTLS, obligatoire hors démonstration.</summary>
    public bool EnableSsl { get; set; } = true;

    /// <summary>Compte du relais SMTP, facultatif (MailPit n'en demande pas).</summary>
    public string? UserName { get; set; }

    /// <summary>Mot de passe du relais (secret : jamais dans un fichier versionné).</summary>
    public string? Password { get; set; }

    /// <summary>Adresse d'expédition de l'installation, la même pour tous les dossiers.</summary>
    public string FromAddress { get; set; } = "";

    /// <summary>Nom affiché quand le cas d'usage n'en donne pas.</summary>
    public string FromName { get; set; } = "Lettra";

    /// <summary>Délai maximal d'un envoi.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Adresse publique de Lettra, citée dans les invitations (« ouvrez … et connectez-vous »). Sans elle, le message
    /// invite à ouvrir Lettra sans lien.
    /// </summary>
    public Uri? PublicUrl { get; set; }
}
