using Lettra.Application.Abstractions.External;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;

namespace Lettra.Infrastructure.External.Payees;

/// <summary>
/// Banque simulée pour la vérification du bénéficiaire, en attendant un prestataire (la vraie vérification passe par un
/// établissement de paiement, donc par la société). Rien ne quitte le poste. Son registre connaît trois comptes de
/// démonstration ; pour tout autre IBAN, elle répond que le nom donné est celui du titulaire.
/// </summary>
internal sealed class SimulatedPayeeVerification : IPayeeVerification
{
    /// <summary>Les comptes de démonstration : un compte allemand au nom d'un tiers, un titulaire à une lettre près, un compte de banque fermée.</summary>
    public static readonly IReadOnlyDictionary<string, string?> Registry = new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["DE89370400440532013000"] = "Schmidt Handels GmbH",
        ["FR7630006000011234567890189"] = "Minoterie Dupuy",
        ["FR1420041010050500013M02606"] = null,
    };

    public Task<Result<PayeeVerificationResult>> VerifyAsync(Iban iban, string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(iban);
        if (!Registry.TryGetValue(iban.Value, out string? holder))
        {
            return Task.FromResult<Result<PayeeVerificationResult>>(new PayeeVerificationResult(PayeeMatch.Match));
        }

        if (holder is null)
        {
            return Task.FromResult<Result<PayeeVerificationResult>>(new PayeeVerificationResult(PayeeMatch.NotPossible));
        }

        PayeeMatch result = PayeeNames.Compare(name, holder);
        return Task.FromResult<Result<PayeeVerificationResult>>(
            new PayeeVerificationResult(result, result == PayeeMatch.CloseMatch ? holder : null));
    }
}
