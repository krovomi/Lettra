using System.Globalization;
using Lettra.Contracts.Speech;
using Lettra.UI.Shared.Localization;
using Microsoft.Extensions.Localization;

namespace Lettra.UI.Shared.Voice;

/// <summary>
/// Les commandes vocales que l'écran ouvert sait exécuter (vague 5, lane F). Une page ou un composant s'inscrit pour
/// un type de commande (la grille des lignes pour « ajouter une ligne » et « remise ») et se désinscrit en partant.
/// Une commande qui a besoin d'un écran pas encore ouvert (« devis pour Dupont, trois heures de conseil ») est
/// gardée ici le temps d'y aller.
/// </summary>
public sealed class VoiceCommands
{
    private readonly List<(VoiceCommandKind Kind, Func<VoiceCommand, Task<bool>> Run)> _handlers = [];

    /// <summary>Commande laissée pour l'écran suivant (les lignes d'un devis dicté).</summary>
    public VoiceCommand? Deferred { get; private set; }

    public IDisposable Register(VoiceCommandKind kind, Func<VoiceCommand, Task<bool>> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        (VoiceCommandKind, Func<VoiceCommand, Task<bool>>) entry = (kind, run);
        _handlers.Add(entry);
        return new Registration(() => _handlers.Remove(entry));
    }

    public bool CanRun(VoiceCommandKind kind) => _handlers.Any(h => h.Kind == kind);

    /// <summary>Exécute par le dernier inscrit (l'écran le plus récent). Faux si personne ne sait le faire ici.</summary>
    public async Task<bool> TryRunAsync(VoiceCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        for (int i = _handlers.Count - 1; i >= 0; i--)
        {
            if (_handlers[i].Kind == command.Kind && await _handlers[i].Run(command))
            {
                return true;
            }
        }

        return false;
    }

    public void Defer(VoiceCommand command) => Deferred = command;

    /// <summary>Reprend la commande gardée, une seule fois.</summary>
    public VoiceCommand? TakeDeferred()
    {
        VoiceCommand? command = Deferred;
        Deferred = null;
        return command;
    }

    /// <summary>Écran d'une liste ou d'une page nommée à la voix ; <c>null</c> pour l'assistant (c'est un panneau).</summary>
    public static string? ListRoute(VoiceTarget target) => target switch
    {
        VoiceTarget.Home => "",
        VoiceTarget.Quotes => "quotes",
        VoiceTarget.Invoices => "invoices",
        VoiceTarget.CreditNotes => "credit-notes",
        VoiceTarget.Customers => "customers",
        VoiceTarget.Suppliers => "suppliers",
        VoiceTarget.Catalog => "catalog",
        VoiceTarget.Fec => "exports/fec",
        VoiceTarget.Alerts => "alerts",
        VoiceTarget.Sync => "synchronisation",
        VoiceTarget.Settings => "settings/company",
        _ => null,
    };

    /// <summary>Écran de création ; une liste qui porte son propre formulaire (clients, catalogue) ouvre la liste.</summary>
    public static string? CreateRoute(VoiceTarget target) => target switch
    {
        VoiceTarget.Quotes => "quotes/new",
        VoiceTarget.Invoices => "invoices/new",
        VoiceTarget.Customers or VoiceTarget.Suppliers or VoiceTarget.Catalog => ListRoute(target),
        _ => null,
    };

    /// <summary>La commande comprise, en une phrase, affichée avant d'être exécutée.</summary>
    public static string Describe(VoiceCommand command, IStringLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(localizer);
        CultureInfo culture = CultureInfo.CurrentCulture;
        string target = command.Target is { } t ? localizer[$"Voice.Target.{t}"] : localizer["Voice.Target.Everywhere"];
        string text = command.Kind switch
        {
            VoiceCommandKind.Navigate => string.Format(culture, localizer["Voice.Do.Navigate"], target),
            VoiceCommandKind.Create => string.Format(culture, localizer["Voice.Do.Create"], target),
            VoiceCommandKind.Search => string.Format(culture, localizer["Voice.Do.Search"], command.Term, target),
            VoiceCommandKind.AddLine => string.Join(" ; ", command.Lines.Select(l => Line(l, localizer, culture))),
            VoiceCommandKind.ApplyDiscount => string.Format(culture, localizer["Voice.Do.Discount"], command.DiscountPercent?.ToString("0.##", culture)),
            VoiceCommandKind.DraftQuote => string.Format(culture, localizer["Voice.Do.DraftQuote"], command.Customer ?? "?", command.Lines.Count),
            VoiceCommandKind.Validate => localizer["Voice.Do.Validate"],
            VoiceCommandKind.Save => localizer["Voice.Do.Save"],
            VoiceCommandKind.Cancel => localizer["Voice.Do.Cancel"],
            _ => localizer["Voice.Do.Help"],
        };
        return command.Filter is { } filter && command.Kind == VoiceCommandKind.Navigate
            ? $"{text} ({localizer[$"Voice.Filter.{filter}"]})"
            : text;
    }

    private static string Line(VoiceLine line, IStringLocalizer localizer, CultureInfo culture)
    {
        string quantity = line.Quantity.ToString("0.###", culture) + line.Unit switch
        {
            "HUR" => " h",
            "DAY" => " j",
            _ => string.Empty,
        };
        string price = line.UnitPrice is { } p
            ? string.Format(culture, localizer[line.PriceIncludesVat == true ? "Voice.Do.PriceTtc" : "Voice.Do.PriceHt"], p.ToString("#,##0.00", culture))
            : string.Empty;
        return string.Format(culture, localizer["Voice.Do.AddLine"], quantity, line.Designation, price).TrimEnd();
    }

    private sealed class Registration(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose()
        {
            _dispose?.Invoke();
            _dispose = null;
        }
    }
}
