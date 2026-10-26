using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Abstractions.External;

/// <summary>Réponse de la banque du bénéficiaire. <paramref name="SuggestedName"/> : le titulaire, donné seulement quand le nom correspond presque.</summary>
public sealed record PayeeVerificationResult(PayeeMatch Result, string? SuggestedName = null);

/// <summary>
/// Vérification du bénéficiaire avant un virement SEPA (« Verification of Payee », règlement (UE) 2024/886) : la banque du
/// titulaire de l'IBAN dit si le nom donné est le sien. Seuls l'IBAN et le nom du fournisseur quittent le dossier.
/// Sans prestataire branché, l'adaptateur simulé de Lettra.Infrastructure.External répond ; hors ligne :
/// <c>Directory.Unavailable</c>.
/// </summary>
public interface IPayeeVerification
{
    Task<Result<PayeeVerificationResult>> VerifyAsync(Iban iban, string name, CancellationToken cancellationToken);
}

/// <summary>Sans adaptateur : la vérification n'est pas possible, le contrôle local reste disponible.</summary>
internal sealed class NoPayeeVerification : IPayeeVerification
{
    public Task<Result<PayeeVerificationResult>> VerifyAsync(Iban iban, string name, CancellationToken cancellationToken) =>
        Task.FromResult<Result<PayeeVerificationResult>>(new PayeeVerificationResult(PayeeMatch.NotPossible));
}
