namespace Lettra.Exports.Domain.Platform;

/// <summary>Voie de transmission d'une pièce à la plateforme agréée.</summary>
public enum PlatformChannel
{
    /// <summary>Facture électronique : client professionnel établi en France (SIREN).</summary>
    EInvoice = 1,

    /// <summary>E-reporting de la transaction : client particulier ou établi hors de France.</summary>
    EReporting = 2,
}
