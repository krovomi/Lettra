using System.Diagnostics;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Diagnostics;
using Lettra.Application.Messaging;
using Lettra.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Lettra.Application.Behaviors;

/// <summary>
/// Comportement le plus externe : trace, métriques (techniques et métier, <see cref="BusinessMetrics"/>) et journal
/// de chaque cas d'usage.
/// Ne journalise que le nom du cas d'usage, l'issue, la durée et les codes d'erreur : jamais
/// le contenu de la requête, qui est une donnée de dossier.
/// </summary>
internal sealed partial class LoggingBehavior<TRequest, TResult>(
    ILogger<LoggingBehavior<TRequest, TResult>> logger,
    ITenantContext tenant) : IPipelineBehavior<TRequest, TResult>
    where TRequest : notnull
{
    public async Task<TResult> HandleAsync(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        string useCase = typeof(TRequest).Name;
        using Activity? activity = LettraTelemetry.ActivitySource.StartActivity(useCase);
        activity?.SetTag(LettraTelemetry.Attributes.UseCase, useCase);
        if (tenant.IsResolved)
        {
            activity?.SetTag(LettraTelemetry.Attributes.DossierId, tenant.Current.DossierId.Value);
        }

        long started = Stopwatch.GetTimestamp();
        string outcome = LettraTelemetry.Outcomes.Error;
        object? finished = null;
        IReadOnlyList<Error> errors = [];
        try
        {
            TResult result = await next().ConfigureAwait(false);
            finished = result;
            errors = ResultFactory.ErrorsOf(result);
            if (errors.Count == 0)
            {
                outcome = LettraTelemetry.Outcomes.Success;
                LogSucceeded(logger, useCase);
            }
            else
            {
                outcome = LettraTelemetry.Outcomes.Failure;
                string codes = string.Join(',', errors.Select(e => e.Code).Distinct());
                activity?.SetTag(LettraTelemetry.Attributes.ErrorCode, errors[0].Code);
                LogFailed(logger, useCase, codes);
            }

            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            // Le message d'exception peut contenir des données (ex. erreur SQL citant une valeur) : on ne journalise que le type.
            LogCrashed(logger, useCase, exception.GetType().FullName ?? exception.GetType().Name);
            throw;
        }
        finally
        {
            TagList tags = new()
            {
                { LettraTelemetry.Attributes.UseCase, useCase },
                { LettraTelemetry.Attributes.Outcome, outcome },
            };
            LettraTelemetry.UseCases.Add(1, tags);
            LettraTelemetry.UseCaseDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, tags);
            activity?.SetTag(LettraTelemetry.Attributes.Outcome, outcome);
            BusinessMetrics.Record(useCase, request, outcome, finished, errors);
        }
    }

    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Cas d'usage {UseCase} réussi")]
    private static partial void LogSucceeded(ILogger logger, string useCase);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Cas d'usage {UseCase} refusé : {ErrorCodes}")]
    private static partial void LogFailed(ILogger logger, string useCase, string errorCodes);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Error, Message = "Cas d'usage {UseCase} en erreur : {ExceptionType}")]
    private static partial void LogCrashed(ILogger logger, string useCase, string exceptionType);
}
