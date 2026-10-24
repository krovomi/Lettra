using System.Diagnostics;
using System.Diagnostics.Metrics;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Speech;
using Lettra.Application.Diagnostics;
using Lettra.Contracts.Common;
using Lettra.Contracts.Speech;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Speech;

namespace Lettra.Application.Features.Speech;

/// <summary>Codes d'erreur de la voix.</summary>
public static class SpeechErrors
{
    public const string DisabledCode = ErrorCodes.Speech.Disabled;
    public const string EngineUnavailableCode = ErrorCodes.Speech.EngineUnavailable;
    public const string ModelUnavailableCode = ErrorCodes.Speech.ModelUnavailable;
    public const string AudioInvalidCode = ErrorCodes.Speech.AudioInvalid;
    public const string AudioTooLongCode = ErrorCodes.Speech.AudioTooLong;
    public const string LanguageUnsupportedCode = ErrorCodes.Speech.LanguageUnsupported;
    public const string NothingHeardCode = ErrorCodes.Speech.NothingHeard;

    public static Error Disabled => new(DisabledCode, ErrorKind.Forbidden);

    public static Error EngineUnavailable => Error.Unavailable(EngineUnavailableCode);

    public static Error ModelUnavailable => Error.Unavailable(ModelUnavailableCode);

    public static Error NothingHeard => Error.Validation(NothingHeardCode);
}

/// <summary>
/// Transcrire un enregistrement (WAV PCM 16 bits mono 16 kHz) du dossier ouvert : dictée mise en forme, et en mode
/// commande la commande reconnue par la grammaire déterministe. Rien n'est enregistré : ni l'audio ni le texte.
/// La permission est celle de lire le dossier : la voix ne fait que remplacer le clavier, chaque action qu'elle
/// déclenche repasse par son propre cas d'usage et sa propre permission.
/// </summary>
public sealed record TranscribeSpeechQuery(DossierId Dossier, ReadOnlyMemory<byte> Wav, string Language, SpeechMode Mode)
    : IQuery<Result<TranscriptionResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

internal sealed class TranscribeSpeechValidator(SpeechSettings settings) : IValidator<TranscribeSpeechQuery>
{
    public IReadOnlyList<Error> Validate(TranscribeSpeechQuery instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!settings.Enabled)
        {
            // Désactivée : le refus vient du handler, avec son code propre.
            return [];
        }

        List<Error> errors = [];
        if (!Enum.IsDefined(instance.Mode))
        {
            errors.Add(Error.Validation(ErrorCodes.Validation.Invalid, nameof(instance.Mode)));
        }

        if (SpokenLanguages.FromCode(instance.Language) is not SpokenLanguage language || !settings.Languages.Contains(language.ToCode()))
        {
            errors.Add(Error.Validation(ErrorCodes.Speech.LanguageUnsupported, nameof(instance.Language)));
        }

        if (instance.Wav.IsEmpty)
        {
            errors.Add(Error.Validation(ErrorCodes.Validation.Required, nameof(instance.Wav)));
        }
        else if (instance.Wav.Length > SpeechAudio.MaxBytes(settings.MaxSeconds))
        {
            errors.Add(Error.Validation(ErrorCodes.Speech.AudioTooLong, nameof(instance.Wav)));
        }

        return errors;
    }
}

internal sealed class TranscribeSpeechHandler(SpeechSettings settings, ISpeechToText engine)
    : IQueryHandler<TranscribeSpeechQuery, Result<TranscriptionResponse>>
{
    public async Task<Result<TranscriptionResponse>> HandleAsync(TranscribeSpeechQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!settings.Enabled)
        {
            return SpeechErrors.Disabled;
        }

        SpokenLanguage language = SpokenLanguages.FromCode(query.Language) ?? SpokenLanguage.French;
        Result<SpeechAudio> audio = SpeechAudio.FromWav(query.Wav, settings.MaxSeconds);
        if (audio.IsFailure)
        {
            return Result<TranscriptionResponse>.Failure(audio.Errors);
        }

        long started = Stopwatch.GetTimestamp();
        string outcome = LettraTelemetry.Outcomes.Failure;
        try
        {
            if (audio.Value.IsSilent())
            {
                return SpeechErrors.NothingHeard;
            }

            SpeechRequest request = new(language.ToCode(), SpeechPrompts.For(language));
            Result<SpeechTranscript> transcript = await engine.TranscribeAsync(audio.Value, request, cancellationToken).ConfigureAwait(false);
            if (transcript.IsFailure)
            {
                outcome = LettraTelemetry.Outcomes.Error;
                return Result<TranscriptionResponse>.Failure(transcript.Errors);
            }

            string heard = TranscriptCleaner.Clean(transcript.Value.Text);
            string text = DictationFormatter.Format(heard, language);
            if (text.Length == 0)
            {
                return SpeechErrors.NothingHeard;
            }

            VoiceCommand? command = query.Mode == SpeechMode.Command ? VoiceCommandParser.Parse(heard, language) : null;
            outcome = LettraTelemetry.Outcomes.Success;
            return new TranscriptionResponse(text, language.ToCode(), Math.Round(audio.Value.Duration.TotalSeconds, 2), command);
        }
        finally
        {
            SpeechTelemetry.Record(engine.Name, query.Mode, outcome, audio.Value.Duration, Stopwatch.GetElapsedTime(started));
        }
    }
}

/// <summary>État de la voix sur ce déploiement : l'interface n'affiche le micro que si elle est active.</summary>
public sealed record GetSpeechStatusQuery(DossierId Dossier) : IQuery<Result<SpeechStatusResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

internal sealed class GetSpeechStatusHandler(SpeechSettings settings, ISpeechToText engine) : IQueryHandler<GetSpeechStatusQuery, Result<SpeechStatusResponse>>
{
    public async Task<Result<SpeechStatusResponse>> HandleAsync(GetSpeechStatusQuery query, CancellationToken cancellationToken) =>
        settings.Enabled
            ? settings.ToResponse(await engine.IsReadyAsync(cancellationToken).ConfigureAwait(false))
            : settings.ToResponse();
}

/// <summary>Vocabulaire du métier donné au moteur : il l'oriente vers « devis », « HT », « SIRET » plutôt que vers des homophones.</summary>
public static class SpeechPrompts
{
    public static string For(SpokenLanguage language) => language == SpokenLanguage.English
        ? "Quote, invoice, credit note, down payment, excluding VAT, including VAT, VAT, discount, customer, supplier, catalog."
        : "Devis, facture, avoir, acompte, HT, TTC, TVA, remise, forfait, client, fournisseur, catalogue, SIRET, FEC.";
}

/// <summary>
/// Mesures de la voix (annexe G) : nombre de transcriptions par moteur, mode et issue, durée de l'audio et du calcul.
/// Aucune étiquette ne porte le texte, la langue dite ou le dossier.
/// </summary>
internal static class SpeechTelemetry
{
    private static readonly Counter<long> Transcriptions = LettraTelemetry.Meter.CreateCounter<long>(
        "lettra.speech.transcription.count", unit: "{transcription}", description: "Transcriptions vocales.");

    private static readonly Histogram<double> AudioDuration = LettraTelemetry.Meter.CreateHistogram<double>(
        "lettra.speech.audio.duration", unit: "s", description: "Durée des enregistrements transcrits.");

    private static readonly Histogram<double> TranscriptionDuration = LettraTelemetry.Meter.CreateHistogram<double>(
        "lettra.speech.transcription.duration", unit: "s", description: "Temps de transcription.");

    public static void Record(string engine, SpeechMode mode, string outcome, TimeSpan audio, TimeSpan elapsed)
    {
        TagList tags = new()
        {
            { LettraTelemetry.Attributes.SpeechEngine, engine },
            { LettraTelemetry.Attributes.SpeechMode, mode == SpeechMode.Command ? "command" : "dictation" },
            { LettraTelemetry.Attributes.Outcome, outcome },
        };
        Transcriptions.Add(1, tags);
        AudioDuration.Record(audio.TotalSeconds, tags);
        TranscriptionDuration.Record(elapsed.TotalSeconds, tags);
    }
}
