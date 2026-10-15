using System.Text.Json.Serialization;

namespace Lettra.Contracts.Exports;

/// <summary>Syntaxe de la facture électronique EN 16931 d'une pièce validée.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EInvoiceFormatDto>))]
public enum EInvoiceFormatDto
{
    /// <summary>UN/CEFACT CII D16B, profil EN 16931 : le XML d'un Factur-X (<c>factur-x.xml</c>).</summary>
    Cii = 1,

    /// <summary>OASIS UBL 2.1 (Invoice ou CreditNote).</summary>
    Ubl = 2,
}
