using System.Globalization;
using Lettra.Application.Abstractions.External;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Domain.Vigilance;

namespace Lettra.Infrastructure.External.Registers;

/// <summary>
/// Registres officiels simulés, en attendant les accès réels (INPI pour les bénéficiaires effectifs, DG Trésor pour le gel
/// des avoirs, DILA pour le BODACC, URSSAF pour les attestations) : rien ne quitte le poste, toutes les réponses sont
/// fictives et se déduisent du SIREN, toujours les mêmes pour un même SIREN. Les règles sont décrites avec chaque registre
/// pour qu'une démonstration sache quoi montrer.
/// </summary>
internal sealed class SimulatedOfficialRegisters(TimeProvider clock)
    : IBeneficialOwnerRegister, IAssetFreezeRegister, ILegalNoticeFeed, IUrssafAttestationCheck
{
    /// <summary>La personne fictive du registre des gels que les bénéficiaires effectifs simulés rencontrent parfois.</summary>
    public const string FrozenOwner = "Viktor Fictivenko";

    /// <summary>Un code d'attestation qui commence ainsi n'est pas reconnu par l'URSSAF simulée.</summary>
    public const string UnknownCodePrefix = "ZZ";

    /// <summary>Le registre national des gels simulé : des noms inventés, sans rapport avec des personnes réelles.</summary>
    public static readonly IReadOnlyList<FrozenEntry> FrozenList =
    [
        new(FrozenOwner, ["Viktor Fiktivenko", "V. Fictivenko"]),
        new("Boris Inventov", []),
        new("Société d'Import Imaginaire", ["SII Imaginaire"]),
        new("Groupe Chimère Export", []),
    ];

    private static readonly string[] FirstNames = ["Claire", "Hugo", "Nadia", "Thomas", "Inès", "Paul", "Léa", "Karim", "Sophie", "Julien", "Amélie", "Marc"];

    private static readonly string[] LastNames = ["Bertin", "Lagarde", "Morel", "Benali", "Fournier", "Caron", "Perrin", "Lemoine", "Vasseur", "Roussel", "Gauthier", "Marchal"];

    /// <summary>
    /// Un à trois bénéficiaires, détenteurs du capital ; pour un SIREN sur dix-sept, l'un d'eux est la personne fictive du
    /// registre des gels (<see cref="FrozenOwner"/>).
    /// </summary>
    public Task<Result<IReadOnlyList<RegisteredOwner>>> FindAsync(Siren siren, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(siren);
        long n = Number(siren);
        int count = 1 + (int)(n % 3);
        decimal[] shares = count switch
        {
            1 => [100m],
            2 => [60m, 40m],
            _ => [34m, 33m, 33m],
        };
        List<RegisteredOwner> owners =
        [
            .. Enumerable.Range(0, count).Select(i => new RegisteredOwner(
                $"{FirstNames[(int)((n / 7) + (i * 5)) % FirstNames.Length]} {LastNames[(int)((n / 11) + (i * 7)) % LastNames.Length]}",
                OwnershipKind.Capital,
                shares[i])),
        ];
        if (n % 17 == 3)
        {
            owners[^1] = owners[^1] with { Name = FrozenOwner };
        }

        return Task.FromResult<Result<IReadOnlyList<RegisteredOwner>>>(owners);
    }

    public Task<Result<IReadOnlyList<FrozenEntry>>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<Result<IReadOnlyList<FrozenEntry>>>(Result<IReadOnlyList<FrozenEntry>>.Success(FrozenList));

    /// <summary>
    /// Un SIREN sur onze a une procédure collective publiée il y a vingt à soixante-dix-neuf jours ; un sur dix-neuf est
    /// radié. Les autres n'ont aucune annonce qui change la relation.
    /// </summary>
    public Task<Result<IReadOnlyList<LegalNotice>>> ListAsync(Siren siren, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(siren);
        long n = Number(siren);
        DateOnly today = LegalCalendar.DateOf(clock.GetUtcNow());
        List<LegalNotice> notices = [];
        if (n % 11 == 4)
        {
            notices.Add(new LegalNotice(LegalNoticeKind.CollectiveProceeding, today.AddDays(-(20 + (int)(n % 60))),
                "Jugement d'ouverture d'une procédure de redressement judiciaire"));
        }
        else if (n % 19 == 7)
        {
            notices.Add(new LegalNotice(LegalNoticeKind.StruckOff, today.AddDays(-(30 + (int)(n % 90))), "Radiation du registre du commerce et des sociétés"));
        }

        return Task.FromResult<Result<IReadOnlyList<LegalNotice>>>(notices);
    }

    /// <summary>Tout code est reconnu, sauf ceux qui commencent par <see cref="UnknownCodePrefix"/>.</summary>
    public Task<Result<UrssafAnswer>> VerifyAsync(Siren siren, string securityCode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(securityCode);
        UrssafAnswer answer = securityCode.StartsWith(UnknownCodePrefix, StringComparison.OrdinalIgnoreCase) ? UrssafAnswer.NotRecognized : UrssafAnswer.Recognized;
        return Task.FromResult<Result<UrssafAnswer>>(answer);
    }

    private static long Number(Siren siren) => long.Parse(siren.Value, CultureInfo.InvariantCulture);
}
