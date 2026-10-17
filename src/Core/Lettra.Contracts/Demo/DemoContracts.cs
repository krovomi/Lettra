using System.Text.Json.Serialization;

namespace Lettra.Contracts.Demo;

/// <summary>Le jeu de démonstration peut-il être chargé ici (démonstration activée, hors Production) ?</summary>
public sealed record DemoDataStatusResponse(bool Available);

/// <summary>Issue d'un chargement du jeu de démonstration (mêmes valeurs que <c>Lettra.DemoData.DemoSeedStatus</c>).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DemoSeedStatusDto>))]
public enum DemoSeedStatusDto
{
    Seeded = 1,
    NotRequested = 2,
    RefusedInProduction = 3,
    DossierNotEmpty = 4,
    NoUser = 5,
    NoDossier = 6,
}

/// <summary>Ce que le chargement a mis dans le dossier ouvert (et les dossiers créés en plus).</summary>
public sealed record DemoSeedResponse(DemoSeedStatusDto Status, int Dossiers, int Customers, int Quotes, int Invoices);
