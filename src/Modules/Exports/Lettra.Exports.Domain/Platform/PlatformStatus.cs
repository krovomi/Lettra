namespace Lettra.Exports.Domain.Platform;

/// <summary>
/// Statut du cycle de vie d'une facture électronique à la plateforme agréée (norme AFNOR XP Z12-012). La valeur est le
/// code du statut, la même que celle de <c>PlatformStatusDto</c> et que celle enregistrée en base. Déposée, Rejetée,
/// Refusée et Encaissée sont les quatre statuts obligatoires.
/// </summary>
public enum PlatformStatus
{
    /// <summary>Déposée : la plateforme du vendeur a reçu la facture.</summary>
    Deposited = 200,

    /// <summary>Émise par la plateforme du vendeur.</summary>
    Issued = 201,

    /// <summary>Reçue par la plateforme de l'acheteur.</summary>
    Received = 202,

    /// <summary>Mise à disposition de l'acheteur.</summary>
    MadeAvailable = 203,

    /// <summary>Prise en charge par l'acheteur.</summary>
    InProgress = 204,

    /// <summary>Approuvée par l'acheteur.</summary>
    Approved = 205,

    /// <summary>Approuvée partiellement.</summary>
    PartiallyApproved = 206,

    /// <summary>En litige.</summary>
    Disputed = 207,

    /// <summary>Suspendue : l'acheteur attend un complément.</summary>
    Suspended = 208,

    /// <summary>Complétée : le vendeur a apporté le complément.</summary>
    Completed = 209,

    /// <summary>Refusée par l'acheteur (définitif) : la pièce se corrige par un avoir.</summary>
    Refused = 210,

    /// <summary>Paiement transmis par l'acheteur.</summary>
    PaymentSent = 211,

    /// <summary>Encaissée : le vendeur déclare l'encaissement (définitif).</summary>
    Collected = 212,

    /// <summary>Rejetée par la plateforme (format, contrôles, doublon) : définitif, la pièce peut être retransmise.</summary>
    Rejected = 213,
}
