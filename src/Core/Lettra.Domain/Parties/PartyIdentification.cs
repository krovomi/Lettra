using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Parties;

/// <summary>Identifiants d'entreprise d'un tiers (client ou fournisseur), lus et contrôlés ensemble.</summary>
/// <param name="Siren">SIREN saisi, ou déduit du SIRET.</param>
/// <param name="Siret">SIRET de l'établissement facturé.</param>
/// <param name="VatNumber">Numéro de TVA intracommunautaire.</param>
public sealed record PartyIdentification(Siren? Siren, Siret? Siret, VatNumber? VatNumber)
{
    /// <summary>
    /// Lit les trois identifiants en ajoutant les erreurs à <paramref name="errors"/> :
    /// le SIRET porte le SIREN (on le déduit ou on vérifie la cohérence), et un numéro de TVA
    /// français porte aussi le SIREN (<c>FR</c> + clé + SIREN).
    /// </summary>
    /// <param name="siren">SIREN saisi (espaces tolérés).</param>
    /// <param name="siret">SIRET saisi.</param>
    /// <param name="vatNumber">Numéro de TVA saisi.</param>
    /// <param name="siretSirenMismatch">Erreur propre au module appelant (ex. <c>Customer.SiretSirenMismatch</c>).</param>
    /// <param name="errors">Liste où s'ajoutent les erreurs, rattachées à leur champ.</param>
    public static PartyIdentification Read(string? siren, string? siret, string? vatNumber, Error siretSirenMismatch, List<Error> errors)
    {
        ArgumentNullException.ThrowIfNull(siretSirenMismatch);
        ArgumentNullException.ThrowIfNull(errors);

        Siren? parsedSiren = Optional(siren, SharedKernel.Siren.Create, nameof(Siren), errors);
        Siret? parsedSiret = Optional(siret, SharedKernel.Siret.Create, nameof(Siret), errors);
        VatNumber? parsedVat = Optional(vatNumber, SharedKernel.VatNumber.Create, nameof(VatNumber), errors);

        if (parsedSiret is not null)
        {
            if (parsedSiren is null)
            {
                parsedSiren = parsedSiret.Siren;
            }
            else if (parsedSiren != parsedSiret.Siren)
            {
                errors.Add(siretSirenMismatch);
            }
        }

        if (parsedVat is not null && parsedSiren is not null && parsedVat.FrenchSiren is string vatSiren && vatSiren != parsedSiren.Value)
        {
            errors.Add(PartyErrors.VatNumberSirenMismatch);
        }

        return new PartyIdentification(parsedSiren, parsedSiret, parsedVat);
    }

    /// <summary>Vrai si une erreur porte déjà sur l'un des champs d'identification.</summary>
    public static bool HasIdentificationError(List<Error> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return errors.Exists(e => e.Target is nameof(Siren) or nameof(Siret));
    }

    /// <summary>Lit un objet-valeur facultatif : vide = absent ; invalide = erreur rattachée au champ.</summary>
    internal static T? Optional<T>(string? raw, Func<string?, Result<T>> factory, string target, List<Error> errors)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        Result<T> result = factory(raw);
        if (result.IsSuccess)
        {
            return result.Value;
        }

        errors.AddRange(result.Errors.Select(e => e.For(target)));
        return null;
    }
}
