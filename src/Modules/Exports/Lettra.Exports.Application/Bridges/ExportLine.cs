namespace Lettra.Exports.Application.Bridges;

/// <summary>
/// Une ligne d'écriture prête à exporter, telle que le FEC l'écrit : journal, numéro d'écriture dans le journal, compte
/// général et, pour le compte client (411), le compte auxiliaire du client. Montants au débit ou au crédit, jamais
/// recalculés.
/// </summary>
public sealed record ExportLine(
    string Journal,
    string JournalLabel,
    int EntryNumber,
    DateOnly Date,
    string Account,
    string AccountLabel,
    Guid? CustomerId,
    string? AuxiliaryAccount,
    string? AuxiliaryLabel,
    string Piece,
    string Label,
    decimal Debit,
    decimal Credit,
    string? LetteringCode,
    DateOnly? LetteringDate)
{
    /// <summary>Sens de la ligne : débit si le débit est renseigné, crédit sinon.</summary>
    public bool IsDebit => Debit != 0m;

    /// <summary>Le montant de la ligne, dans son sens.</summary>
    public decimal Amount => IsDebit ? Debit : Credit;
}
