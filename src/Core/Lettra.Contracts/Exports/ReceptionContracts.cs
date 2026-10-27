using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Lettra.Contracts.Common;

namespace Lettra.Contracts.Exports;

/// <summary>Nature d'une facture fournisseur reçue (code de type BT-3).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReceivedInvoiceKindDto>))]
public enum ReceivedInvoiceKindDto
{
    Invoice = 1,
    DownPayment = 2,
    CreditNote = 3,
}

/// <summary>Une facture fournisseur reçue, dans la liste.</summary>
/// <param name="Id">Son identifiant dans le dossier.</param>
/// <param name="Reference">Sa référence à la plateforme agréée.</param>
/// <param name="Kind">Facture, acompte ou avoir.</param>
/// <param name="Number">Le numéro donné par le fournisseur.</param>
/// <param name="IssueDate">Sa date d'émission.</param>
/// <param name="DueDate">Son échéance, si elle en porte une.</param>
/// <param name="SupplierName">Le fournisseur, tel que la facture le nomme.</param>
/// <param name="SupplierSiren">Son SIREN.</param>
/// <param name="SupplierId">Le fournisseur du dossier qui porte ce SIREN, s'il y en a un.</param>
/// <param name="TotalExclTax">Total HT.</param>
/// <param name="TotalVat">TVA.</param>
/// <param name="TotalInclTax">Total TTC.</param>
/// <param name="Currency">La devise (EUR).</param>
/// <param name="Status">Le dernier statut du cycle de vie.</param>
/// <param name="ReceivedAt">La mise à disposition par la plateforme.</param>
public sealed record ReceivedInvoiceSummary(
    Guid Id,
    string Reference,
    ReceivedInvoiceKindDto Kind,
    string Number,
    DateOnly IssueDate,
    DateOnly? DueDate,
    string SupplierName,
    string? SupplierSiren,
    Guid? SupplierId,
    decimal TotalExclTax,
    decimal TotalVat,
    decimal TotalInclTax,
    string Currency,
    PlatformStatusDto Status,
    DateTimeOffset ReceivedAt);

/// <summary>Une ligne de la facture reçue, telle que le fournisseur l'a écrite.</summary>
public sealed record ReceivedInvoiceLineResponse(string Name, decimal Quantity, string? UnitCode, decimal NetPrice, decimal NetAmount, string VatCategory, decimal? VatRate);

/// <summary>Une ventilation de TVA de la facture reçue.</summary>
public sealed record ReceivedInvoiceVatResponse(string Category, decimal? Rate, decimal Basis, decimal Amount);

/// <summary>
/// Une facture reçue en détail : l'en-tête, les lignes et la TVA relues dans le fichier reçu, l'historique de ses
/// statuts et les réponses que l'acheteur peut encore donner.
/// </summary>
public sealed record ReceivedInvoiceResponse(
    ReceivedInvoiceSummary Summary,
    string Platform,
    EInvoiceFormatDto Format,
    string FileName,
    string? SupplierVatNumber,
    string? BusinessProcess,
    IReadOnlyList<ReceivedInvoiceLineResponse> Lines,
    IReadOnlyList<ReceivedInvoiceVatResponse> Vat,
    IReadOnlyList<string> Notes,
    IReadOnlyList<PlatformStatusEventResponse> History,
    IReadOnlyList<PlatformStatusDto> AvailableResponses);

/// <summary>
/// La réception des factures fournisseurs du dossier : la plateforme, l'inscription de l'entreprise à l'annuaire et les
/// dates de la réforme qui la concernent.
/// </summary>
/// <param name="PlatformConfigured">Une plateforme agréée est configurée.</param>
/// <param name="CompanySiren">Le SIREN de la fiche entreprise : sans lui, rien ne se reçoit.</param>
/// <param name="Directory">L'inscription de l'entreprise à l'annuaire, si elle y figure.</param>
/// <param name="ReceivingFrom">Date à laquelle toute entreprise doit recevoir ses factures électroniques.</param>
/// <param name="IssuingFrom">Date à laquelle l'entreprise émet, fait son e-reporting et porte les nouvelles mentions.</param>
/// <param name="Invoices">Les factures reçues, les plus récentes d'abord.</param>
public sealed record ReceivedInvoicesResponse(
    bool PlatformConfigured,
    string? CompanySiren,
    DirectoryEntryResponse? Directory,
    DateOnly ReceivingFrom,
    DateOnly IssuingFrom,
    IReadOnlyList<ReceivedInvoiceSummary> Invoices)
{
    /// <summary>Nombre de factures reçues lues au plus (les plus récentes).</summary>
    public const int MaxInvoices = 500;
}

/// <summary>Le relevé des factures mises à disposition par la plateforme.</summary>
/// <param name="Received">Nouvelles factures enregistrées dans le dossier.</param>
/// <param name="Unreadable">Fichiers que Lettra n'a pas su lire (laissés à la plateforme).</param>
public sealed record FetchReceivedInvoicesResponse(int Received, int Unreadable);

/// <summary>
/// La réponse de l'acheteur à une facture reçue : prise en charge, approuvée, en litige ou refusée (avec un motif), ou
/// paiement transmis (avec le montant).
/// </summary>
public sealed record RespondToReceivedInvoiceRequest
{
    public PlatformStatusDto Status { get; set; }

    [StringLength(500, ErrorMessage = ErrorCodes.Reception.ReasonTooLong)]
    public string? Reason { get; set; }

    [Range(0.01d, 999_999_999.99d, ErrorMessage = ErrorCodes.Reception.PaymentAmountInvalid)]
    public decimal? Amount { get; set; }
}

/// <summary>
/// L'inscription d'une entreprise à l'annuaire central de la facturation électronique : son adresse de réception et la
/// plateforme agréée qu'elle a choisie.
/// </summary>
/// <param name="Siren">Le SIREN recherché.</param>
/// <param name="ElectronicAddress">L'adresse électronique de réception (schéma 0225, « SIREN » ou « SIREN_SIRET »).</param>
/// <param name="PlatformName">La plateforme agréée qui reçoit pour elle.</param>
/// <param name="Since">Date de son inscription.</param>
public sealed record DirectoryEntryResponse(string Siren, string ElectronicAddress, string PlatformName, DateOnly Since);

/// <summary>
/// Le résultat d'une recherche à l'annuaire : l'inscription, ou rien si l'entreprise n'y figure pas.
/// <paramref name="Consulted"/> est faux quand aucune plateforme n'est configurée : l'annuaire n'a pas été interrogé.
/// </summary>
public sealed record DirectoryLookupResponse(string Siren, DirectoryEntryResponse? Entry, bool Consulted = true);
