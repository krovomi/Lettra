using Lettra.Api.Infrastructure;
using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Speech;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Speech;
using Lettra.Contracts.Common;
using Lettra.Contracts.Speech;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Api.Endpoints;

/// <summary>
/// Dictée et commandes vocales depuis le navigateur (vague 5, lane D). L'audio part de l'enregistreur de l'interface
/// vers cette route, puis vers le serveur Whisper de la cellule : jamais vers un service tiers. Il n'est ni enregistré
/// ni journalisé.
/// </summary>
internal static class SpeechEndpoints
{
    public const string WavContentType = "audio/wav";

    public static RouteGroupBuilder MapSpeech(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/voix").WithTags("Voix");

        group.MapGet("/statut", StatusAsync)
            .WithName("GetSpeechStatus")
            .RequirePermission(Permission.DossierLire)
            .Produces<SpeechStatusResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/transcription", TranscribeAsync)
            .WithName("TranscribeSpeech")
            .RequirePermission(Permission.DossierLire)
            .Accepts<byte[]>(WavContentType, "audio/x-wav", "audio/wave")
            .Produces<TranscriptionResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return group;
    }

    private static async Task<IResult> StatusAsync(ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        Result<SpeechStatusResponse> result = await queries
            .QueryAsync(new GetSpeechStatusQuery(tenant.Current.DossierId), cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Errors.ToProblem();
    }

    /// <summary>
    /// Corps : le fichier WAV (<c>audio/wav</c> ; les autres types reçoivent 415 du routage). <paramref name="mode"/> : <c>Dictation</c> (par défaut) ou
    /// <c>Command</c> ; <paramref name="langue"/> : <c>fr</c> (par défaut) ou <c>en</c>.
    /// </summary>
    private static async Task<IResult> TranscribeAsync(
        HttpRequest request,
        ITenantContext tenant,
        SpeechSettings settings,
        IQueryDispatcher queries,
        CancellationToken cancellationToken,
        SpeechMode mode = SpeechMode.Dictation,
        string langue = "fr")
    {
        if (!settings.Enabled)
        {
            return new[] { SpeechErrors.Disabled }.ToProblem();
        }

        // Lecture bornée : un corps plus long que la durée maximale n'est pas lu jusqu'au bout.
        int limit = SpeechAudio.MaxBytes(settings.MaxSeconds);
        if (request.ContentLength > limit)
        {
            return new[] { Error.Validation(ErrorCodes.Speech.AudioTooLong) }.ToProblem();
        }

        using MemoryStream body = new();
        byte[] buffer = new byte[16 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (body.Length + read > limit)
            {
                return new[] { Error.Validation(ErrorCodes.Speech.AudioTooLong) }.ToProblem();
            }

            body.Write(buffer, 0, read);
        }

        TranscribeSpeechQuery query = new(tenant.Current.DossierId, body.ToArray(), langue, mode);
        Result<TranscriptionResponse> result = await queries.QueryAsync(query, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            return TypedResults.Ok(result.Value);
        }

        // Moteur arrêté ou modèle absent : le service est indisponible, la requête n'y est pour rien.
        return result.Errors[0].Code is SpeechErrors.EngineUnavailableCode or SpeechErrors.ModelUnavailableCode
            ? TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: result.Errors[0].Code,
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = result.Errors[0].Code })
            : result.Errors.ToProblem();
    }
}
