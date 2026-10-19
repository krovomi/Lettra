// PWA : le service worker ne met en cache que l'enveloppe de l'application (contexte sécurisé requis).
// Fichier séparé et non script en ligne : la politique de contenu (CSP) de l'API n'admet que les scripts de l'origine.
//
// Mise à jour : une nouvelle version publiée (git pull puis up --build) prend la main dès qu'elle est en cache
// (skipWaiting et clients.claim dans service-worker.published.js). Ici, la page la charge :
// - dans les 10 premières secondes, sans saisie commencée : rechargement automatique ;
// - plus tard, ou après une saisie : bandeau « Nouvelle version de Lettra disponible — Recharger », rien n'est perdu.
// Le navigateur ne cherche une nouvelle version qu'à la navigation : on la demande aussi au retour sur l'onglet, au
// retour du réseau et tous les quarts d'heure.
if ('serviceWorker' in navigator) {
    const autoReloadWindowMs = 10_000;
    let controlled = navigator.serviceWorker.controller !== null;
    let typed = false;
    let reloading = false;
    addEventListener('input', () => { typed = true; }, { capture: true, once: true });

    navigator.serviceWorker.addEventListener('controllerchange', () => {
        if (!controlled) {
            // Première installation : la page est déjà à jour.
            controlled = true;
            return;
        }
        if (reloading) {
            return;
        }
        if (!typed && performance.now() < autoReloadWindowMs) {
            reloading = true;
            location.reload();
        } else {
            showUpdateBanner();
        }
    });

    navigator.serviceWorker.register('service-worker.js', { updateViaCache: 'none' }).then(registration => {
        const check = () => registration.update().catch(() => { /* serveur arrêté ou hors ligne : prochain essai */ });
        document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'visible') check(); });
        addEventListener('focus', check);
        addEventListener('online', check);
        setInterval(check, 15 * 60 * 1000);
    }, () => { /* pas de service worker (navigation privée, contexte non sécurisé) : l'application marche sans */ });
}

// Même aspect que les notifications de l'application (lettra.css), mais en haut de l'écran pour ne pas les recouvrir.
function showUpdateBanner() {
    if (document.getElementById('lettra-update')) {
        return;
    }
    const english = (document.documentElement.lang || navigator.language || '').toLowerCase().startsWith('en');
    const region = document.createElement('div');
    region.id = 'lettra-update';
    region.className = 'lt-toasts';
    region.setAttribute('role', 'status');
    region.setAttribute('aria-live', 'polite');
    region.style.top = 'calc(var(--space-3) + env(safe-area-inset-top, 0px))';
    region.style.bottom = 'auto';
    region.style.zIndex = '70';

    const toast = document.createElement('div');
    toast.className = 'lt-toast is-info';
    toast.style.gridTemplateColumns = 'minmax(0, 1fr) auto';

    const text = document.createElement('span');
    text.className = 'lt-toast-text';
    text.textContent = english ? 'A new version of Lettra is available.' : 'Nouvelle version de Lettra disponible.';

    const button = document.createElement('button');
    button.type = 'button';
    button.id = 'lettra-update-reload';
    button.className = 'lettra-button la-small';
    button.textContent = english ? 'Reload' : 'Recharger';
    button.addEventListener('click', () => location.reload());

    toast.append(text, button);
    region.append(toast);
    document.body.append(region);
}
