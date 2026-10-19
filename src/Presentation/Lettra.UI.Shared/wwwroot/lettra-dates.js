// Calendrier d'un champ de date (Components/Dates) : s'ouvre sous le champ, aligné à gauche, ou à droite quand il
// sortirait de la fenêtre (champ « au » d'une période, dernière colonne d'un formulaire). Sur téléphone, le CSS en fait
// une feuille en bas de l'écran et ce réglage ne compte pas.
export function placeCalendar(calendar) {
    if (!calendar) {
        return;
    }

    calendar.classList.remove('is-end');
    const box = calendar.getBoundingClientRect();
    if (box.right > document.documentElement.clientWidth - 8) {
        calendar.classList.add('is-end');
    }
}
