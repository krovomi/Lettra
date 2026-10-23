# Dates et nombres dans la langue de l'écran

Lettra s'affiche en français ou en anglais. Les dates et les montants suivent cette langue partout, dans le navigateur
(PWA) comme dans l'application MAUI, sur ordinateur, tablette et téléphone.

| Langue de l'écran | Date | Montant | Saisie d'une date | Calendrier |
|---|---|---|---|---|
| Français | 19/10/2026 | 15 128,00 | jj/mm/aaaa | commence le lundi, « Octobre 2026 » |
| Anglais | 10/19/2026 | 15,128.00 | mm/dd/yyyy | commence le dimanche, « October 2026 » |

## Ce qui était faux

Les dates affichées (listes, fiches) étaient déjà au bon format. Les **champs de saisie de date** ne l'étaient pas : ils
utilisaient le champ natif du navigateur (`<input type="date">`), dont le format suit la langue du navigateur ou du
système, jamais celle de la page. Un Chrome ou un Windows en anglais montrait donc `mm/dd/yyyy` dans un écran en
français (filtres « Date d'émission », échéance, FEC, kit de contrôle fiscal…). Aucun réglage de la page ne change ce
format : il fallait un champ à nous.

En français, le séparateur des milliers est une espace fine insécable (U+202F), presque invisible dans la police de
Lettra : « 15 128,00 » se lisait « 15128,00 ».

## Comment c'est fait

- **Une seule culture pour l'écran** : `LettraCultures.Resolve` choisit la langue (celle choisie dans « Mes
  préférences », sinon celle du navigateur ou du système, si elle est prise en charge ; sinon le français),
  `LettraCultures.Apply` la pose pour les textes et les formats (`Localization/SharedResource.cs`). C'est le point
  d'entrée à appeler pour changer de langue : « Mes préférences » passe par lui (`ScreenLanguage`, voir
  [preferences-utilisateur.md](preferences-utilisateur.md)) ; l'application MAUI part encore de la langue du système
  au démarrage. `Apply` passe la culture par `LettraCultures.ForScreen`, qui remplace l'espace fine des milliers par
  une espace insécable ; les PDF, exports et l'API ne passent pas par là et gardent leurs formats.
- **`DateField`** (`Components/Dates`) remplace tous les champs de date. Il affiche la date courte de la culture, lit
  la saisie dans l'ordre de la langue et ouvre un calendrier localisé. Il se lie comme un champ Blazor :
  `<DateField Id="due-date" @bind-Value="_dueDate" Min="..." Max="..." Disabled="..." />` ; les attributs inconnus
  (`aria-label`, `required`) vont au champ texte. Pour une date obligatoire (`DateOnly`), garder l'ancienne valeur :
  `ValueChanged="d => _from = d ?? _from"`.
- **`DateText`** fait la mise en forme et la lecture, sans Blazor (testée seule).
- Les formats fixés par la loi ou les fichiers ne changent pas : dates ISO de l'API et des adresses des pages, FEC, Factur-X et
  UBL, CSV. Le PDF d'un document suit la langue préférée du client, comme avant.

## Saisir une date

On tape au clavier ou on choisit dans le calendrier (bouton à droite du champ).

- Acceptés en français : `19/10/2026`, `19.10.26`, `19-10-2026`, `19 10 2026`, `19102026`, `191026`, `19/10` (année en
  cours), `2026-10-19`, `19 octobre 2026`. En anglais, mois puis jour : `10/19/2026`, `10192026`, `10/19`.
- La date est remise au format de la langue en quittant le champ. Une saisie illisible ou hors des bornes reste
  affichée, encadrée en rouge, avec le format attendu ou les bornes en dessous ; la valeur n'est pas changée.
- Effacer le champ efface la date ; une date obligatoire (FEC, période) revient à sa valeur.
- Sur téléphone, le clavier est numérique (`inputmode="numeric"`) : les chiffres seuls suffisent (`19102026`).

## Calendrier

- **Ordinateur et tablette** : s'ouvre sous le champ, aligné à droite quand il sortirait de la fenêtre
  (`lettra-dates.js`).
- **Téléphone** (moins de 640 px) : feuille en bas de l'écran sur un voile, jours de 44 px.
- **Clavier** : flèches (jour, semaine), Pg préc. / Pg suiv. (mois, avec Maj : année), Début / Fin (semaine), Entrée
  (choisir), Échap (fermer). Un clic à côté ferme aussi. « Aujourd'hui » choisit la date du jour.
- **Accessibilité** : dialogue nommé, chaque jour porte sa date en toutes lettres (« lundi 19 octobre 2026 »),
  `aria-current="date"` sur aujourd'hui, `aria-pressed` sur la date choisie, jours hors bornes désactivés.

## Tests

- `tests/Lettra.UI.Tests/Dates/DateFieldTests.cs` : formats et lecture en français et en anglais, erreurs, bornes,
  calendrier (premier jour, noms, clavier, bornes), filtres des listes et export FEC dans les deux langues.
- `tests/Lettra.UI.Tests/Dates/ScreenCultureTests.cs` : culture des écrans (milliers en français, formats anglais).
- Vérifié dans la démonstration Docker (Chromium) : liste des factures, nouvelle facture, FEC et Vue cabinet, en
  français et en anglais, à 1440 px et 390 px.
