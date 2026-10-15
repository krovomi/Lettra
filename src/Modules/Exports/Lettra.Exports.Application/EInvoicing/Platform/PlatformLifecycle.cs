using Lettra.Contracts.Exports;
using static Lettra.Contracts.Exports.PlatformStatusDto;

namespace Lettra.Exports.Application.EInvoicing.Platform;

/// <summary>
/// La machine d'états du cycle de vie (XP Z12-012). Seuls Déposée, Rejetée, Refusée et Encaissée sont obligatoires : les
/// statuts facultatifs peuvent manquer, une transition n'exige donc pas le statut qui la précède d'ordinaire. Refusée et
/// Rejetée sont définitifs ; Encaissée se répète tant que tout le TTC n'est pas encaissé (paiements partiels).
/// </summary>
public static class PlatformLifecycle
{
    private static readonly PlatformStatusDto[] BeforeAvailability = [Deposited, Issued, Received];

    private static readonly PlatformStatusDto[] BeforeDecision = [Deposited, Issued, Received, MadeAvailable, InProgress];

    private static readonly Dictionary<PlatformStatusDto, PlatformStatusDto[]> AllowedFrom = new()
    {
        // Le circuit des plateformes : toujours vers l'avant.
        [Issued] = [Deposited],
        [Received] = [Deposited, Issued],
        [MadeAvailable] = [Deposited, Issued, Received],
        [Rejected] = BeforeAvailability,

        // Les réponses de l'acheteur.
        [InProgress] = [Deposited, Issued, Received, MadeAvailable],
        [Approved] = [.. BeforeDecision, PartiallyApproved, Disputed, Suspended, Completed],
        [PartiallyApproved] = [.. BeforeDecision, Disputed, Suspended, Completed],
        [Disputed] = [.. BeforeDecision, Approved, PartiallyApproved, Suspended, Completed],
        [Suspended] = [.. BeforeDecision, Disputed, Completed],
        [Completed] = [Suspended],
        [Refused] = [.. BeforeDecision, Disputed, Suspended, Completed],
        [PaymentSent] = [.. BeforeDecision, Approved, PartiallyApproved, Disputed, Suspended, Completed, PaymentSent],

        // L'encaissement, déclaré par le vendeur à tout moment avant un statut définitif.
        [Collected] = [.. BeforeDecision, Approved, PartiallyApproved, Disputed, Suspended, Completed, PaymentSent, Collected],
    };

    /// <summary>Refusée et Rejetée closent la transmission ; Encaissée la clôt quand tout le TTC est encaissé.</summary>
    public static bool IsTerminal(PlatformStatusDto status) => status is Refused or Rejected;

    /// <summary>Statut posé par le vendeur, pas par les plateformes ni l'acheteur.</summary>
    public static bool IsDeclaredBySeller(PlatformStatusDto status) => status is Collected;

    public static bool CanMove(PlatformStatusDto from, PlatformStatusDto to) =>
        !IsTerminal(from) && AllowedFrom.TryGetValue(to, out PlatformStatusDto[]? origins) && origins.Contains(from);
}
