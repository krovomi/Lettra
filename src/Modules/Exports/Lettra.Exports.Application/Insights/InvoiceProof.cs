using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Contracts.Insights;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.EInvoicing;

namespace Lettra.Exports.Application.Insights;

/// <summary>La preuve d'authenticité d'une pièce validée, à imprimer en QR code.</summary>
public sealed record GetInvoiceProofQuery(DossierId Dossier, InvoiceId InvoiceId) : IQuery<Result<InvoiceProofResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>La vérification d'une preuve lue sur une facture, contre le registre des clés et les pièces du dossier.</summary>
public sealed record VerifyInvoiceProofQuery(DossierId Dossier, string Code) : IQuery<Result<InvoiceProofCheckResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>Le registre des clés publiques du dossier, pour vérifier une preuve sans Lettra.</summary>
public sealed record ListInvoiceProofKeysQuery(DossierId Dossier) : IQuery<Result<IReadOnlyList<InvoiceProofKeyResponse>>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>Une pièce validée du dossier, telle que la preuve la décrit.</summary>
public sealed record IssuedInvoice(InvoiceId Id, string Number, DateOnly IssueDate, decimal TotalInclTax, decimal CreditedInclTax);

/// <summary>Les pièces validées du dossier, par numéro.</summary>
public interface IIssuedInvoiceReader
{
    Task<IssuedInvoice?> FindByNumberAsync(string number, CancellationToken cancellationToken);
}

/// <summary>
/// Le texte d'une preuve : <c>LETTRA-PREUVE-1;SIREN;numéro;AAAAMMJJ;total TTC;clé;signature</c>. Les six premiers champs
/// sont signés tels quels (UTF-8) ; la signature ECDSA P-256 / SHA-256 au format DER est en base 64 URL sans remplissage.
/// Le numéro est échappé (<c>%3B</c> pour un point-virgule) : le texte reste lisible et se découpe sans ambiguïté.
/// </summary>
public static partial class InvoiceProofCode
{
    public const string Prefix = "LETTRA-PREUVE-1";

    /// <summary>Le texte signé : tout sauf la signature.</summary>
    public static string Statement(string? siren, string number, DateOnly issueDate, decimal totalInclTax, string keyId) =>
        string.Join(
            ';',
            Prefix,
            siren ?? string.Empty,
            Uri.EscapeDataString(number),
            issueDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            totalInclTax.ToString("0.00", CultureInfo.InvariantCulture),
            keyId);

    public static string Code(string statement, byte[] signature) => $"{statement};{Base64Url(signature)}";

    /// <summary>Les champs d'une preuve, ou rien si le texte n'en est pas une.</summary>
    public static ParsedProof? Parse(string? code)
    {
        string[] fields = (code ?? string.Empty).Trim().Split(';');
        if (fields.Length != 7 || fields[0] != Prefix
            || (fields[1].Length > 0 && !SirenPattern().IsMatch(fields[1]))
            || !DateOnly.TryParseExact(fields[3], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
            || !AmountPattern().IsMatch(fields[4])
            || !KeyIdPattern().IsMatch(fields[5])
            || FromBase64Url(fields[6]) is not { Length: > 0 } signature)
        {
            return null;
        }

        string number = Uri.UnescapeDataString(fields[2]);
        return number.Length == 0
            ? null
            : new ParsedProof(
                string.Join(';', fields[..6]),
                fields[1].Length > 0 ? fields[1] : null,
                number,
                date,
                decimal.Parse(fields[4], NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture),
                fields[5],
                signature);
    }

    /// <summary>Une clé publique (SubjectPublicKeyInfo, base 64) au format PEM, lisible par OpenSSL.</summary>
    public static string Pem(string publicKey)
    {
        StringBuilder pem = new("-----BEGIN PUBLIC KEY-----\n");
        for (int i = 0; i < publicKey.Length; i += 64)
        {
            pem.Append(publicKey.AsSpan(i, Math.Min(64, publicKey.Length - i))).Append('\n');
        }

        return pem.Append("-----END PUBLIC KEY-----\n").ToString();
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[]? FromBase64Url(string text)
    {
        if (text.Length is 0 or > 200)
        {
            return null;
        }

        string padded = text.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - (padded.Length % 4)) % 4);
        try
        {
            return Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"^\d{9}$", RegexOptions.CultureInvariant)]
    private static partial Regex SirenPattern();

    [GeneratedRegex(@"^-?\d{1,12}\.\d{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex AmountPattern();

    [GeneratedRegex("^p256-[0-9a-f]{16}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyIdPattern();
}

/// <summary>Une preuve découpée : le texte signé, ses champs et la signature.</summary>
public sealed record ParsedProof(string Statement, string? Siren, string Number, DateOnly IssueDate, decimal TotalInclTax, string KeyId, byte[] Signature);

internal sealed class GetInvoiceProofHandler(IInvoiceRepository invoices, ICompanyProfileRepository profiles, IDocumentSigner signer)
    : IQueryHandler<GetInvoiceProofQuery, Result<InvoiceProofResponse>>
{
    public async Task<Result<InvoiceProofResponse>> HandleAsync(GetInvoiceProofQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Invoice? invoice = await invoices.FindAsync(query.InvoiceId, cancellationToken).ConfigureAwait(false);
        if (invoice is null)
        {
            return InvoiceErrors.NotFound;
        }

        if (invoice.IsDraft || invoice is not { Number: { } number, IssueDate: { } issueDate })
        {
            return EInvoiceErrors.NotValidated;
        }

        CompanyProfile? profile = await profiles.FindAsync(cancellationToken).ConfigureAwait(false);
        string? siren = profile?.Siren?.Value;

        Result<DocumentSignature> signed = await signer.SignAsync(
            keyId => InvoiceProofCode.Statement(siren, number, issueDate, invoice.TotalInclTax, keyId), cancellationToken).ConfigureAwait(false);
        return signed.IsSuccess
            ? new InvoiceProofResponse(invoice.Id.Value, number, InvoiceProofCode.Code(signed.Value.Statement, signed.Value.Signature), signed.Value.KeyId)
            : Result<InvoiceProofResponse>.Failure(signed.Errors);
    }
}

internal sealed class VerifyInvoiceProofHandler(IDocumentSigner signer, IIssuedInvoiceReader issued, ICompanyProfileRepository profiles)
    : IQueryHandler<VerifyInvoiceProofQuery, Result<InvoiceProofCheckResponse>>
{
    public async Task<Result<InvoiceProofCheckResponse>> HandleAsync(VerifyInvoiceProofQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (InvoiceProofCode.Parse(query.Code) is not { } proof)
        {
            return new InvoiceProofCheckResponse(InvoiceProofVerdictDto.Unreadable, null, null, null, null, null, null, []);
        }

        Result<DocumentSignatureCheck> check = await signer.VerifyAsync(proof.KeyId, proof.Statement, proof.Signature, cancellationToken).ConfigureAwait(false);
        if (check.IsFailure)
        {
            return Result<InvoiceProofCheckResponse>.Failure(check.Errors);
        }

        InvoiceProofCheckResponse read = new(InvoiceProofVerdictDto.Unreadable, proof.Siren, proof.Number, proof.IssueDate, proof.TotalInclTax, proof.KeyId, null, []);
        if (check.Value != DocumentSignatureCheck.Valid)
        {
            return read with { Verdict = check.Value == DocumentSignatureCheck.UnknownKey ? InvoiceProofVerdictDto.Foreign : InvoiceProofVerdictDto.Forged };
        }

        IssuedInvoice? invoice = await issued.FindByNumberAsync(proof.Number, cancellationToken).ConfigureAwait(false);
        if (invoice is null)
        {
            return read with { Verdict = InvoiceProofVerdictDto.Mismatch, Differences = ["Missing"] };
        }

        CompanyProfile? profile = await profiles.FindAsync(cancellationToken).ConfigureAwait(false);
        List<string> differences = [];
        if (invoice.IssueDate != proof.IssueDate)
        {
            differences.Add("IssueDate");
        }

        if (invoice.TotalInclTax != proof.TotalInclTax)
        {
            differences.Add("TotalInclTax");
        }

        if (proof.Siren is not null && profile?.Siren?.Value is { } siren && siren != proof.Siren)
        {
            differences.Add("Siren");
        }

        return read with
        {
            Verdict = differences.Count == 0 ? InvoiceProofVerdictDto.Authentic : InvoiceProofVerdictDto.Mismatch,
            InvoiceId = invoice.Id.Value,
            Differences = differences,
            FullyCredited = invoice.TotalInclTax > 0m && invoice.CreditedInclTax >= invoice.TotalInclTax,
        };
    }
}

internal sealed class ListInvoiceProofKeysHandler(IIntegrityKeyring keyring)
    : IQueryHandler<ListInvoiceProofKeysQuery, Result<IReadOnlyList<InvoiceProofKeyResponse>>>
{
    public async Task<Result<IReadOnlyList<InvoiceProofKeyResponse>>> HandleAsync(ListInvoiceProofKeysQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<IntegrityKeyVersion> versions = await keyring.ListAsync(cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<InvoiceProofKeyResponse>>.Success(
            [.. versions.Select(v => new InvoiceProofKeyResponse(v.Version, v.KeyId, InvoiceProofCode.Pem(v.PublicKey), v.CreatedAt, v.RetiredAt))]);
    }
}
