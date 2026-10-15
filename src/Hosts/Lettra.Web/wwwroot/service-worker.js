// Développement : toujours le réseau, aucun cache (le service worker publié est service-worker.published.js).
// Le gestionnaire vide suffit pour que l'application reste installable.
self.addEventListener('fetch', () => { });
