using Lettra.Contracts.Common;
using Lettra.Contracts.Vigilance;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Tests.Vigilance;

/// <summary>Tiers fictifs : la passerelle garde les lignes et rend, à chaque vérification, la réponse préparée pour ce tiers.</summary>
internal sealed class FakeThirdPartyChecksGateway(DateTimeOffset now) : IThirdPartyChecksGateway
{
    public List<ThirdPartyCheckRow> Rows { get; } = [];

    /// <summary>Ce que les registres répondront pour un tiers ; rien de préparé : rien à signaler.</summary>
    public Dictionary<Guid, Func<ThirdPartyCheckRow, ThirdPartyCheckRow>> Answers { get; } = [];

    public List<Guid> Checked { get; } = [];

    public IReadOnlyList<ErrorDetail>? NextListErrors { get; set; }

    /// <summary>Refuse la vérification qui porte ce numéro (1 pour la première).</summary>
    public (int Call, string Code)? FailOn { get; set; }

    public ThirdPartyCheckRow Add(string name, ThirdPartyKindDto kind = ThirdPartyKindDto.Customer, string? siren = "732829320",
        ThirdPartyCheckStatusDto status = ThirdPartyCheckStatusDto.NeverChecked, DateTimeOffset? checkedAt = null)
    {
        ThirdPartyCheckRow row = new(kind, Guid.CreateVersion7(), name, siren, status, checkedAt,
            checkedAt is null ? null : 1, checkedAt is null ? null : FreezeScreeningDto.Clear, null, null, null, null, null);
        Rows.Add(row);
        return row;
    }

    public Task<OperationResult<ThirdPartyChecksResponse>> ListAsync(CancellationToken cancellationToken)
    {
        if (NextListErrors is { } errors)
        {
            NextListErrors = null;
            return Task.FromResult(OperationResult<ThirdPartyChecksResponse>.Failure(errors));
        }

        return Task.FromResult(OperationResult<ThirdPartyChecksResponse>.Success(new ThirdPartyChecksResponse([.. Rows])));
    }

    public Task<OperationResult<ThirdPartyCheckRow>> CheckAsync(ThirdPartyKindDto kind, Guid partyId, CancellationToken cancellationToken)
    {
        Checked.Add(partyId);
        if (FailOn is { } fail && fail.Call == Checked.Count)
        {
            return Task.FromResult(OperationResult<ThirdPartyCheckRow>.Failure(fail.Code));
        }

        int index = Rows.FindIndex(r => r.PartyId == partyId);
        ThirdPartyCheckRow clear = Rows[index] with
        {
            Status = ThirdPartyCheckStatusDto.Clear,
            CheckedAt = now,
            OwnersFound = Rows[index].Siren is null ? null : 2,
            Freeze = FreezeScreeningDto.Clear,
        };
        Rows[index] = Answers.TryGetValue(partyId, out Func<ThirdPartyCheckRow, ThirdPartyCheckRow>? answer) ? answer(clear) : clear;
        return Task.FromResult(OperationResult<ThirdPartyCheckRow>.Success(Rows[index]));
    }
}
