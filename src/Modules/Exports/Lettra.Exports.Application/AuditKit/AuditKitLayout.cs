using System.Globalization;

namespace Lettra.Exports.Application.AuditKit;

/// <summary>
/// L'arborescence du kit contrôle fiscal. L'écran qui y ajoute les PDF lit leurs chemins dans le manifeste. Version
/// du format : <see cref="Format"/> (à changer si l'arborescence change).
/// </summary>
public static class AuditKitLayout
{
    public const string Format = "lettra-kit-controle-fiscal-v1";
    public const string Manifest = "manifeste.json";
    public const string ReadMe = "LISEZMOI.txt";
    public const string FecReport = "fec/rapport-de-validation.json";
    public const string Attestation = "integrite/attestation.json";
    public const string ClosingsFolder = "integrite/clotures/";
    public const string TrailDescription = "piste-audit-fiable/description.txt";
    public const string TrailPieces = "piste-audit-fiable/pieces.csv";
    public const string TrailPayments = "piste-audit-fiable/encaissements.csv";
    public const string TrailJournal = "piste-audit-fiable/journal.csv";
    public const string Transmissions = "plateforme/transmissions.csv";
    public const string InvoicesFolder = "factures/";

    public static string FileName(string siren, DateOnly from, DateOnly to) =>
        string.Create(CultureInfo.InvariantCulture, $"kit-controle-fiscal-{siren}-{from:yyyyMMdd}-{to:yyyyMMdd}.zip");

    public static string Fec(string fileName) => "fec/" + fileName;

    public static string Closing(string fileName) => ClosingsFolder + fileName;

    /// <summary>Le XML CII (EN 16931) de la pièce : celui que son Factur-X embarque.</summary>
    public static string XmlFile(string number) => $"{InvoicesFolder}{Safe(number)}-cii.xml";

    /// <summary>Le PDF/A-3 Factur-X de la pièce, ajouté par l'écran qui prépare le kit.</summary>
    public static string PdfFile(string number) => $"{InvoicesFolder}{Safe(number)}-factur-x.pdf";

    /// <summary>Un numéro de pièce comme nom de fichier : lettres, chiffres, tiret, point et souligné ; le reste devient « _ ».</summary>
    internal static string Safe(string number) =>
        string.Concat(number.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_'));
}
