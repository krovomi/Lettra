using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace Lettra.Infrastructure.Observability;

/// <summary>Retire des traces les attributs interdits et masque les valeurs qui ressemblent à une donnée de dossier.</summary>
internal sealed class ActivityRedactionProcessor : BaseProcessor<Activity>
{
    public override void OnEnd(Activity data)
    {
        ArgumentNullException.ThrowIfNull(data);
        foreach (KeyValuePair<string, object?> tag in data.TagObjects.ToArray())
        {
            if (DossierDataScrubber.MustDrop(tag.Key))
            {
                data.SetTag(tag.Key, null);
            }
            else if (tag.Value is string text && DossierDataScrubber.LooksSensitive(text))
            {
                data.SetTag(tag.Key, DossierDataScrubber.Scrub(text));
            }
        }

        if (data.StatusDescription is { } status && DossierDataScrubber.LooksSensitive(status))
        {
            data.SetStatus(data.Status, DossierDataScrubber.Scrub(status));
        }
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
