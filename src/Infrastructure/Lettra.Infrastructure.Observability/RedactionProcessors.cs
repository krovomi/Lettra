using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace Lettra.Infrastructure.Observability;

/// <summary>
/// Retire des traces les attributs interdits, la requête des adresses complètes (<c>url.full</c>) et masque les valeurs
/// qui ressemblent à une donnée de dossier. Les événements d'exception (<see cref="Activity.AddException"/>, appelé par
/// exemple par HttpClient) ne se modifient plus une fois ajoutés : ils sont filtrés à leur création, voir
/// <see cref="ExceptionEventRedactor"/>.
/// </summary>
internal sealed class ActivityRedactionProcessor : BaseProcessor<Activity>
{
    private readonly ExceptionEventRedactor _exceptions = new();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _exceptions.Dispose();
        }

        base.Dispose(disposing);
    }

    public override void OnEnd(Activity data)
    {
        ArgumentNullException.ThrowIfNull(data);
        foreach (KeyValuePair<string, object?> tag in data.TagObjects.ToArray())
        {
            if (DossierDataScrubber.MustDrop(tag.Key))
            {
                data.SetTag(tag.Key, null);
            }
            else if (tag.Value is string text)
            {
                string kept = tag.Key == DossierDataScrubber.FullUrlAttribute ? DossierDataScrubber.WithoutQuery(text) : text;
                if (DossierDataScrubber.LooksSensitive(kept))
                {
                    kept = DossierDataScrubber.Scrub(kept);
                }

                if (!ReferenceEquals(kept, text))
                {
                    data.SetTag(tag.Key, kept);
                }
            }
        }

        if (data.StatusDescription is { } status && DossierDataScrubber.LooksSensitive(status))
        {
            data.SetStatus(data.Status, DossierDataScrubber.Scrub(status));
        }
    }
}

/// <summary>
/// Réduit chaque événement d'exception d'une trace à son type : le message et la pile d'appel peuvent citer une valeur
/// (erreur SQL, adresse appelée). <see cref="Activity.AddException"/> consulte les écouteurs avant d'écrire l'événement
/// et ne complète que les attributs absents : on y place donc un masque.
/// </summary>
internal sealed class ExceptionEventRedactor : IDisposable
{
    public const string TypeAttribute = "exception.type";
    public const string MessageAttribute = "exception.message";
    public const string StackTraceAttribute = "exception.stacktrace";

    private readonly ActivityListener _listener;

    public ExceptionEventRedactor()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            ExceptionRecorder = Redact,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    internal static void Redact(Activity activity, Exception exception, ref TagList tags)
    {
        TagList kept = default;
        foreach (KeyValuePair<string, object?> tag in tags)
        {
            if (tag.Key is TypeAttribute or MessageAttribute or StackTraceAttribute || DossierDataScrubber.MustDrop(tag.Key))
            {
                continue;
            }

            kept.Add(tag.Value is string text ? new(tag.Key, DossierDataScrubber.Scrub(text)) : tag);
        }

        kept.Add(new(TypeAttribute, exception.GetType().FullName));
        kept.Add(new(MessageAttribute, DossierDataScrubber.Mask));
        kept.Add(new(StackTraceAttribute, DossierDataScrubber.Mask));
        tags = kept;
    }
}

/// <summary>
/// Même règle pour les journaux : attributs filtrés, message masqué, et l'exception réduite à son type
/// (son message peut citer une valeur, par exemple une erreur SQL).
/// </summary>
internal sealed class LogRedactionProcessor : BaseProcessor<LogRecord>
{
    public const string ExceptionTypeAttribute = "exception.type";

    public override void OnEnd(LogRecord data)
    {
        ArgumentNullException.ThrowIfNull(data);
        List<KeyValuePair<string, object?>> attributes = [];
        foreach (KeyValuePair<string, object?> attribute in data.Attributes ?? [])
        {
            if (DossierDataScrubber.MustDrop(attribute.Key))
            {
                continue;
            }

            attributes.Add(attribute.Value is string text
                ? new(attribute.Key, DossierDataScrubber.Scrub(text))
                : attribute);
        }

        if (data.Exception is { } exception)
        {
            attributes.Add(new(ExceptionTypeAttribute, exception.GetType().FullName));
            data.Exception = null;
        }

        data.Attributes = attributes;
        if (data.FormattedMessage is { } message)
        {
            data.FormattedMessage = DossierDataScrubber.Scrub(message);
        }

        if (data.Body is { } body)
        {
            data.Body = DossierDataScrubber.Scrub(body);
        }
    }
}
