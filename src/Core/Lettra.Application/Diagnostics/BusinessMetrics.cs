using System.Diagnostics;
using Lettra.Application.Features.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Exports;
using Lettra.Domain.Common;

namespace Lettra.Application.Diagnostics;

/// <summary>
/// Métriques métier, comptées en un seul endroit : à la sortie de chaque cas d'usage, par le pipeline
/// (<c>LoggingBehavior</c>), d'après le nom du cas d'usage, son issue et, au besoin, le statut rendu. Les modules n'ont
/// rien à appeler. Seuls des noms et des statuts fixes servent d'étiquettes (<see cref="LettraTelemetry.MetricTags"/>) :
/// ni montant, ni client, ni dossier, ni personne (annexe G).
/// </summary>
internal static class BusinessMetrics
{
    /// <summary>Pièce et étape comptées quand le cas d'usage réussit. La validation vaut pour toute pièce de vente.</summary>
    private static readonly Dictionary<string, (string Type, string Action)> DocumentSteps = new(StringComparer.Ordinal)
    {
        ["CreateQuoteCommand"] = (DocumentTypes.Quote, "created"),
        ["ValidateQuoteCommand"] = (DocumentTypes.Quote, "validated"),
        ["CreateInvoiceCommand"] = (DocumentTypes.Invoice, "created"),
        ["CreateFinalInvoiceCommand"] = (DocumentTypes.Invoice, "created"),
        ["CreateDownPaymentCommand"] = (DocumentTypes.DownPayment, "created"),
        ["ValidateInvoiceCommand"] = (DocumentTypes.Invoice, "validated"),
        ["IssueInvoiceCommand"] = (DocumentTypes.Invoice, "issued"),
        ["ImportOfflineInvoiceCommand"] = (DocumentTypes.Invoice, "imported"),
        ["CreateCreditNoteCommand"] = (DocumentTypes.CreditNote, "created"),
        ["ApproveCreditNoteCommand"] = (DocumentTypes.CreditNote, "approved"),
    };

    /// <summary>Envois à la plateforme agréée : la pièce (dépôt) et l'encaissement.</summary>
    private static readonly Dictionary<string, string> PlatformOperations = new(StringComparer.Ordinal)
    {
        ["TransmitInvoiceCommand"] = "deposit",
        ["ReportPaymentCommand"] = "payment",
    };

    /// <summary>Compte le cas d'usage <paramref name="useCase"/> qui vient de se terminer, s'il a un sens métier.</summary>
    /// <param name="useCase">Nom technique du cas d'usage (nom du type de la requête).</param>
    /// <param name="request">La requête, lue seulement pour son type et ses indicateurs (jamais sa saisie).</param>
    /// <param name="outcome">Issue : <see cref="LettraTelemetry.Outcomes"/>.</param>
    /// <param name="result">Le résultat rendu, <c>null</c> si le cas d'usage a levé une exception.</param>
    /// <param name="errors">Erreurs du résultat (vide en cas de réussite).</param>
    public static void Record(string useCase, object request, string outcome, object? result, IReadOnlyList<Error> errors)
    {
        bool success = outcome == LettraTelemetry.Outcomes.Success;
        if (DocumentSteps.TryGetValue(useCase, out (string Type, string Action) step))
        {
            if (success)
            {
                Count(step.Type, step.Action);
            }

            return;
        }

        switch (useCase)
        {
            case "SetQuoteOutcomeCommand" when success && request is SetQuoteOutcomeCommand decision:
                Count(DocumentTypes.Quote, decision.Accepted ? "accepted" : "refused");
                return;
            case "ExportFecQuery":
                LettraTelemetry.FecExports.Add(1, new TagList { { LettraTelemetry.Attributes.Outcome, outcome } });
                return;
            case "AskAssistantQuery":
                LettraTelemetry.AssistantCalls.Add(1, new TagList
                {
                    { LettraTelemetry.Attributes.Outcome, outcome },
                    { LettraTelemetry.Attributes.ErrorCode, errors.Count > 0 ? errors[0].Code : "none" },
                });
                return;
        }

        if (PlatformOperations.TryGetValue(useCase, out string? operation))
        {
            LettraTelemetry.PlatformTransmissions.Add(1, new TagList
            {
                { LettraTelemetry.Attributes.PlatformOperation, operation },
                { LettraTelemetry.Attributes.PlatformStatus, PlatformStatusOf(outcome, result, errors) },
            });
        }
    }

    /// <summary>
    /// Statut de l'envoi : celui que la plateforme a rendu (<c>deposited</c>, <c>rejected</c>…), <c>queued</c> si
    /// l'envoi attend dans l'outbox (plateforme injoignable), <c>refused</c> si Lettra l'a refusé, <c>error</c> sinon.
    /// </summary>
    internal static string PlatformStatusOf(string outcome, object? result, IReadOnlyList<Error> errors) => result switch
    {
        Result<PlatformTransmissionResponse> { IsSuccess: true } sent => StatusName(sent.Value.Status),
        _ when errors.Any(e => e.Code == ErrorCodes.Platform.Queued) => "queued",
        _ when outcome == LettraTelemetry.Outcomes.Failure => "refused",
        _ => "error",
    };

    /// <summary>Nom du statut en minuscules séparées par « _ » (<c>MadeAvailable</c> → <c>made_available</c>).</summary>
    internal static string StatusName(PlatformStatusDto status)
    {
        string name = status.ToString();
        System.Text.StringBuilder snake = new(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0)
            {
                snake.Append('_');
            }

            snake.Append(char.ToLowerInvariant(name[i]));
        }

        return snake.ToString();
    }

    private static void Count(string type, string action) => LettraTelemetry.Documents.Add(1, new TagList
    {
        { LettraTelemetry.Attributes.DocumentType, type },
        { LettraTelemetry.Attributes.DocumentAction, action },
    });

    private static class DocumentTypes
    {
        public const string Quote = "quote";
        public const string Invoice = "invoice";
        public const string DownPayment = "down_payment";
        public const string CreditNote = "credit_note";
    }
}
