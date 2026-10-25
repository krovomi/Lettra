using System.Text.Json.Serialization;

namespace Lettra.Contracts.Exports;

/// <summary>
/// Logiciel de production du cabinet vers lequel exporter les écritures du dossier (passerelles) : le cabinet garde
/// son outil, Lettra lui remet les écritures dans le format que cet outil importe.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AccountingSoftwareDto>))]
public enum AccountingSoftwareDto
{
    /// <summary>Quadra (Cegid Quadra Expert) : fichier ASCII à positions fixes, lignes « C » (comptes) et « M » (écritures).</summary>
    Quadra = 1,

    /// <summary>Sage 100 Comptabilité : fichier délimité pour l'import paramétrable.</summary>
    Sage = 2,

    /// <summary>Cegid Expert : fichier délimité « écritures Excel ».</summary>
    Cegid = 3,

    /// <summary>ACD : fichier délimité pour l'import paramétrable.</summary>
    Acd = 4,
}
