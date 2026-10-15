using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Mapping;
using Lettra.Contracts.Common;
using Lettra.Contracts.Exports;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Exports.Application.EInvoicing;

/// <summary>Un fichier de facture électronique : nom, type MIME, contenu (UTF-8).</summary>
public sealed record EInvoiceFile(string FileName, string ContentType, byte[] Content);

/// <summary>
/// La facture électronique EN 16931 d'une pièce validée (facture, acompte ou avoir), en CII (le XML d'un Factur-X) ou
/// en UBL. Lecture seule : la pièce figée et l'IBAN de la fiche entreprise suffisent.
/// </summary>
public sealed record GetEInvoiceQuery(DossierId Dossier, InvoiceId InvoiceId, EInvoiceFormatDto Format)
    : IQuery<Result<EInvoiceFile>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

public static class EInvoiceErrors
{
    public const string NotValidatedCode = ErrorCodes.EInvoice.NotValidated;
    public const string FormatUnknownCode = ErrorCodes.EInvoice.FormatUnknown;

    /// <summary>Un brouillon n'a pas de facture électronique : elle naît de la pièce numérotée et figée.</summary>
    public static Error NotValidated => Error.Conflict(NotValidatedCode);

    public static Error FormatUnknown => Error.Validation(FormatUnknownCode, "Format");
}

/// <summary>Écriture d'une pièce dans une syntaxe EN 16931, partagée par la requête et les tests.</summary>
public static class EInvoiceSerializer
{
    public const string FacturXFileName = "factur-x.xml";

    public static byte[] Write(
        Contracts.Billing.InvoiceResponse invoice,
        Contracts.Billing.CompanyProfileResponse? profile,
        EInvoiceFormatDto format,
        DateOnly? precedingInvoiceDate = null)
    {
        EInvoice model = EInvoice.From(invoice, profile, precedingInvoiceDate);
        return format switch
        {
            EInvoiceFormatDto.Cii => CiiWriter.Write(model),
            EInvoiceFormatDto.Ubl => UblWriter.Write(model),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
        };
    }

    /// <summary>La date d'émission de la facture d'origine d'un avoir (BT-26), lue dans le dossier.</summary>
    internal static async Task<DateOnly?> PrecedingInvoiceDateAsync(IInvoiceRepository invoices, Invoice invoice, CancellationToken cancellationToken)
    {
        if (invoice.CreditedInvoiceId is not { } credited)
        {
            return null;
        }

        Invoice? original = await invoices.FindAsync(credited, cancellationToken).ConfigureAwait(false);
        return original?.IssueDate;
    }

    public static string FileNameOf(string number, EInvoiceFormatDto format) =>
        $"{number}-{(format == EInvoiceFormatDto.Cii ? "cii" : "ubl")}.xml";
}

internal sealed class GetEInvoiceHandler(IInvoiceRepository invoices, ICompanyProfileRepository profiles)
    : IQueryHandler<GetEInvoiceQuery, Result<EInvoiceFile>>
{
    public async Task<Result<EInvoiceFile>> HandleAsync(GetEInvoiceQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!Enum.IsDefined(query.Format))
        {
            return EInvoiceErrors.FormatUnknown;
        }

        Invoice? invoice = await invoices.FindAsync(query.InvoiceId, cancellationToken).ConfigureAwait(false);
        if (invoice is null)
        {
            return InvoiceErrors.NotFound;
        }

        if (invoice.IsDraft || invoice.Number is null)
        {
            return EInvoiceErrors.NotValidated;
        }

        CompanyProfile? profile = await profiles.FindAsync(cancellationToken).ConfigureAwait(false);
        DateOnly? preceding = await EInvoiceSerializer.PrecedingInvoiceDateAsync(invoices, invoice, cancellationToken).ConfigureAwait(false);
        byte[] content = EInvoiceSerializer.Write(invoice.ToResponse(), profile?.ToResponse(), query.Format, preceding);
        return new EInvoiceFile(EInvoiceSerializer.FileNameOf(invoice.Number, query.Format), "application/xml", content);
    }
}
