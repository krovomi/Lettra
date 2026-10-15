// PWA : le service worker ne met en cache que l'enveloppe de l'application (contexte sécurisé requis).
// Fichier séparé et non script en ligne : la politique de contenu (CSP) de l'API n'admet que les scripts de l'origine.
if ('serviceWorker' in navigator) {
    navigator.serviceWorker.register('service-worker.js', { updateViaCache: 'none' });
}
