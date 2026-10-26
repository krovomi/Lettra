using Lettra.Application.Abstractions.External;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Mapping;
using Lettra.Contracts.Common;
using Lettra.Contracts.Suppliers;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Parties;

/// <summary>Vérifier auprès de la banque que le nom du fournisseur est bien celui du titulaire de son IBAN, et garder la réponse.</summary>
public sealed record VerifySupplierPayeeCommand(DossierId Dossier, SupplierId SupplierId)
    : ICommand<Result<PayeeCheckResponse>>, IRequirePermission
{
    public Permission Permission => Permission.TiersModifier;
}

/// <summary>Les fournisseurs payables par virement et leurs contrôles avant paiement, à contrôler d'abord par défaut.</summary>
public sealed record ListPayeeChecksQuery(
    DossierId Dossier,
    bool OnlyToReview = true,
    string? Search = null,
    int Page = 1,
    int PageSize = 50,
    PayeeCheckSortDto Sort = PayeeCheckSortDto.Risk,
    SortDirectionDto Direction = SortDirectionDto.Ascending)
    : IQuery<Result<PagedResponse<PayeeCheckRowResponse>>>, IRequirePermission
{
    public const int MaxSearchLength = 100;

    public Permission Permission => Permission.TiersConsulter;
}

internal sealed class ListPayeeChecksValidator : IValidator<ListPayeeChecksQuery>
{
    public IReadOnlyList<Error> Validate(ListPayeeChecksQuery instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        List<Error> errors = [.. Paging.Validate(instance.Page, instance.PageSize, Paging.MaxPageSize)];
        if (instance.Search is { Length: > ListPayeeChecksQuery.MaxSearchLength })
        {
            errors.Add(Error.Validation(ErrorCodes.Validation.TooLong, nameof(instance.Search)));
        }

        errors.AddRange(ListSortRules.Validate(instance.Sort, instance.Direction));
        return errors;
    }
}

internal sealed class VerifySupplierPayeeHandler(
    ISupplierRepository suppliers,
    IPayeeVerification verification,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<VerifySupplierPayeeCommand, Result<PayeeCheckResponse>>
{
    public async Task<Result<PayeeCheckResponse>> HandleAsync(VerifySupplierPayeeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await suppliers.FindAsync(command.SupplierId, cancellationToken).ConfigureAwait(false) is not { } supplier)
        {
            return SupplierErrors.NotFound;
        }

        if (supplier.Iban is not { } iban)
        {
            return SupplierErrors.IbanMissing;
        }

        Result<PayeeVerificationResult> answer = await verification.VerifyAsync(iban, supplier.PayeeName, cancellationToken).ConfigureAwait(false);
        if (answer.IsFailure)
        {
            return Result<PayeeCheckResponse>.Failure(answer.Errors);
        }

        DateTimeOffset now = clock.GetUtcNow();
        Result recorded = supplier.RecordPayeeCheck(answer.Value.Result, answer.Value.SuggestedName, now);
        if (recorded.IsFailure)
        {
            return Result<PayeeCheckResponse>.Failure(recorded.Errors);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return supplier.ToResponse().PayeeCheck!;
    }
}

internal sealed class ListPayeeChecksHandler(IPayeeCheckReadStore store)
    : IQueryHandler<ListPayeeChecksQuery, Result<PagedResponse<PayeeCheckRowResponse>>>
{
    public async Task<Result<PagedResponse<PayeeCheckRowResponse>>> HandleAsync(ListPayeeChecksQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        IReadOnlyList<string> needle = PayeeNames.Words(ListSearch.Normalize(query.Search));
        IEnumerable<PayeeCheckRowResponse> rows = (await store.ListPayeesAsync(cancellationToken).ConfigureAwait(false))
            .Select(PayeeChecks.ToRow)
            .Where(r => !query.OnlyToReview || r.ToReview)
            .Where(r => needle.Count == 0 || PayeeChecks.Matches(r, needle));

        List<PayeeCheckRowResponse> sorted = query.Sort == PayeeCheckSortDto.Name
            ? [.. rows.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)]
            : [.. rows.OrderBy(PayeeChecks.Risk).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)];
        if (query.Direction == SortDirectionDto.Descending)
        {
            sorted.Reverse();
        }

        return new PagedResponse<PayeeCheckRowResponse>(
            [.. sorted.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)], query.Page, query.PageSize, sorted.Count);
    }
}

/// <summary>Règles des contrôles avant paiement : ce qui met un bénéficiaire « à contrôler », et dans quel ordre.</summary>
internal static class PayeeChecks
{
    public static PayeeCheckRowResponse ToRow(SupplierPayeeRow row)
    {
        PayeeMatchDto? holder = row.AccountHolderName is { } name ? (PayeeMatchDto)PayeeNames.Compare(row.Name, name) : null;
        PayeeCheckResponse? check = row.CheckResult is { } result && row.CheckedAt is { } at
            ? new PayeeCheckResponse((PayeeMatchDto)result, at, row.SuggestedName)
            : null;
        bool toReview = row.IbanChangePending || holder == PayeeMatchDto.NoMatch || check?.Result != PayeeMatchDto.Match;
        return new PayeeCheckRowResponse(row.Id.Value, row.Name, row.AccountHolderName, IbanChange.Mask(row.Iban), holder, check, row.IbanChangePending, toReview);
    }

    /// <summary>Du plus risqué au plus sûr : ne correspond pas, IBAN changé, correspond presque, impossible à vérifier, jamais vérifié, vérifié.</summary>
    public static int Risk(PayeeCheckRowResponse row) =>
        row.Check?.Result == PayeeMatchDto.NoMatch || row.HolderNameMatch == PayeeMatchDto.NoMatch ? 0
        : row.IbanChangePending ? 1
        : row.Check?.Result switch
        {
            PayeeMatchDto.CloseMatch => 2,
            PayeeMatchDto.NotPossible => 3,
            null => 4,
            _ => 5,
        };

    public static bool Matches(PayeeCheckRowResponse row, IReadOnlyList<string> needle)
    {
        IReadOnlyList<string> words = [.. PayeeNames.Words(row.Name), .. PayeeNames.Words(row.AccountHolderName)];
        return needle.All(n => words.Any(w => w.Contains(n, StringComparison.Ordinal)));
    }
}
