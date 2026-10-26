using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Lettra.Contracts.Common;

namespace Lettra.Contracts.Missions;

/// <summary>Nature de la mission : présentation des comptes, tenue seule, examen limité.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MissionKindDto>))]
public enum MissionKindDto
{
    Presentation = 1,
    Bookkeeping = 2,
    Review = 3,
}

/// <summary>Un travail compris dans la mission.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MissionServiceDto>))]
public enum MissionServiceDto
{
    Bookkeeping = 1,
    VatReturns = 2,
    AnnualAccounts = 4,
    TaxReturns = 8,
    Payroll = 16,
    Legal = 32,
}

/// <summary>Rythme de facturation du forfait.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FeeScheduleDto>))]
public enum FeeScheduleDto
{
    Monthly = 1,
    Quarterly = 2,
    Yearly = 3,
}

/// <summary>Nature d'un temps passé.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TimeTaskDto>))]
public enum TimeTaskDto
{
    Bookkeeping = 1,
    Vat = 2,
    Closing = 3,
    Payroll = 4,
    Legal = 5,
    Advice = 6,
    Other = 7,
}

/// <summary>La mission du cabinet pour le dossier, telle que la lettre l'engage.</summary>
public sealed record MissionResponse(
    MissionKindDto Kind,
    IReadOnlyList<MissionServiceDto> Services,
    DateOnly StartDate,
    decimal AnnualFeeExclTax,
    FeeScheduleDto Schedule,
    decimal FeePerInstalment,
    decimal? TargetHourlyRate,
    decimal? BudgetHours,
    string ClientSignatory,
    string? Manager,
    DateOnly? SignedOn,
    long Version);

/// <summary>Créer ou modifier la mission (concurrence optimiste par <c>If-Match</c>).</summary>
public sealed record SaveMissionRequest
{
    public MissionKindDto Kind { get; set; } = MissionKindDto.Presentation;

    public IReadOnlyList<MissionServiceDto> Services { get; set; } = [];

    public DateOnly StartDate { get; set; }

    [Range(0.01, 10_000_000, ErrorMessage = ErrorCodes.Mission.FeeInvalid)]
    public decimal AnnualFeeExclTax { get; set; }

    public FeeScheduleDto Schedule { get; set; } = FeeScheduleDto.Monthly;

    [Range(0, 10_000, ErrorMessage = ErrorCodes.Mission.RateInvalid)]
    public decimal? TargetHourlyRate { get; set; }

    [Required(ErrorMessage = ErrorCodes.Mission.SignatoryRequired)]
    [StringLength(120, ErrorMessage = ErrorCodes.Mission.NameTooLong)]
    public string ClientSignatory { get; set; } = string.Empty;

    [StringLength(120, ErrorMessage = ErrorCodes.Mission.NameTooLong)]
    public string? Manager { get; set; }

    public DateOnly? SignedOn { get; set; }
}

/// <summary>Un temps passé ; <paramref name="Mine"/> dit si l'utilisateur peut le supprimer.</summary>
public sealed record TimeEntryResponse(Guid Id, DateOnly Date, Guid UserId, string? UserName, int Minutes, TimeTaskDto Task, string? Note, bool Mine);

/// <summary>Les minutes passées sur l'année de mission pour une nature de travail.</summary>
public sealed record TimeTaskTotal(TimeTaskDto Task, int Minutes);

/// <summary>
/// L'écran « Mission » : la mission (vide tant qu'elle n'est pas saisie), l'année de mission en cours, le temps passé
/// et ce qu'il rapporte (honoraires acquis au prorata des jours écoulés, divisés par les heures), et les temps saisis.
/// Sans mission, l'année est celle des douze derniers mois.
/// </summary>
public sealed record MissionOverviewResponse(
    MissionResponse? Mission,
    string? CabinetName,
    DateOnly YearStart,
    DateOnly YearEnd,
    int MinutesSpent,
    decimal EarnedExclTax,
    decimal? EffectiveHourlyRate,
    IReadOnlyList<TimeTaskTotal> ByTask,
    IReadOnlyList<TimeEntryResponse> Entries);

/// <summary>Saisir un temps passé.</summary>
public sealed record AddTimeEntryRequest
{
    public DateOnly Date { get; set; }

    [Range(1, 720, ErrorMessage = ErrorCodes.Mission.DurationInvalid)]
    public int Minutes { get; set; }

    public TimeTaskDto Task { get; set; } = TimeTaskDto.Bookkeeping;

    [StringLength(200, ErrorMessage = ErrorCodes.Mission.NoteTooLong)]
    public string? Note { get; set; }
}
