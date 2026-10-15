// Service worker publié : met en cache l'enveloppe de l'application (fichiers statiques du manifeste des
// ressources) et rien d'autre. Les réponses de l'API (/api/), du BFF (/bff/) et de tout autre origine
// (Keycloak) passent par le réseau sans jamais être stockées : aucune donnée de dossier dans le cache.
// Voir https://aka.ms/blazor-offline-considerations

self.importScripts('./service-worker-assets.js');
self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', onFetch);

const cacheNamePrefix = 'lettra-shell-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [ /\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff2?$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.svg$/, /\.blat$/, /\.dat$/, /\.webmanifest$/ ];
const offlineAssetsExclude = [ /^service-worker\.js$/ ];

// Chemins servis par le serveur : jamais interceptés, jamais mis en cache.
const networkOnlyPrefixes = [ '/api/', '/bff/', '/health' ];

const base = "/";
const baseUrl = new URL(base, self.origin);
const manifestUrlList = self.assetsManifest.assets.map(asset => new URL(asset.url, baseUrl).href);

async function onInstall(event) {
    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));
    await caches.open(cacheName).then(cache => cache.addAll(assetsRequests));
}

async function onActivate(event) {
    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));
}

function onFetch(event) {
    const url = new URL(event.request.url);
    if (event.request.method !== 'GET'
        || url.origin !== self.origin
        || networkOnlyPrefixes.some(prefix => url.pathname.startsWith(prefix))) {
        // Pas de respondWith : le navigateur traite la requête comme sans service worker.
        return;
    }

    event.respondWith(fromShell(event.request));
}

async function fromShell(request) {
    // Navigation vers une page de l'application : index.html du cache (fonctionne hors ligne).
    const shouldServeIndexHtml = request.mode === 'navigate' && !manifestUrlList.some(url => url === request.url);
    const cache = await caches.open(cacheName);
    const cached = await cache.match(shouldServeIndexHtml ? 'index.html' : request);
    return cached || fetch(request);
}
