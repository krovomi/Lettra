namespace Lettra.Exports.Domain.Platform;

/// <summary>Syntaxe de la facture électronique EN 16931 d'une pièce validée.</summary>
public enum EInvoiceFormat
{
    /// <summary>UN/CEFACT CII D16B, profil EN 16931 : le XML d'un Factur-X (<c>factur-x.xml</c>).</summary>
    Cii = 1,

    /// <summary>OASIS UBL 2.1 (Invoice ou CreditNote).</summary>
    Ubl = 2,
}
