// Test de fumée et de charge légère de la démonstration (deploy/demo) : ~1 minute, quelques utilisateurs virtuels.
//
//   k6 run -e LETTRA_URL=http://localhost:8080 -e LETTRA_SESSION_COOKIE="$(cat session-cookie.txt)" deploy/tests/k6/fumee.js
//
// Scénarios :
//   anonyme    santé, enveloppe de la PWA et fichiers statiques du framework ; l'API refuse (401) sans session ;
//   lectures   lectures authentifiées du dossier de démonstration (session, clients, factures), sous la limite de
//              débit par utilisateur (Lettra:RateLimiting, 300 requêtes par minute par défaut) ;
//   rafale     en fin d'essai, un même utilisateur dépasse sa limite : des 429 doivent apparaître, avec Retry-After
//              et le code stable. Ces 429 sont attendus : ils ne comptent pas comme erreurs.
//
// Session : cookie du BFF exporté par le parcours Playwright (deploy/demo/e2e, mode « session ») après une connexion
// complète mot de passe + TOTP. Sans LETTRA_SESSION_COOKIE, seuls les scénarios anonymes s'exécutent.
import http from 'k6/http';
import { check, fail, group, sleep } from 'k6';
import { Counter, Trend } from 'k6/metrics';
import { textSummary } from 'https://jslib.k6.io/k6-summary/0.1.0/index.js';

const BASE = (__ENV.LETTRA_URL || 'http://localhost:8080').replace(/\/$/, '');
const COOKIE = (__ENV.LETTRA_SESSION_COOKIE || '').trim();
// Dossier de démonstration (deploy/demo/compose.yaml, Lettra__Dossiers__0__Id).
const DOSSIER = __ENV.LETTRA_DOSSIER || '0199e3a0-0000-7000-8000-00000000d051';
const OUT = __ENV.K6_SORTIE || '.';
const AUTH = COOKIE !== '';

const limited = new Counter('lettra_429_rafale');
const admitted = new Counter('lettra_admises_rafale');
const assetBytes = new Trend('lettra_taille_fichier_statique', false);

// Codes attendus par requête : un 401 sans session et un 429 au-delà de la limite sont le comportement voulu.
const ok = http.expectedStatuses(200);
const unauthorized = http.expectedStatuses(401);
const okOrLimited = http.expectedStatuses(200, 429);

const scenarios = {
    anonyme: { executor: 'constant-vus', vus: 4, duration: '50s', exec: 'anonyme', tags: { profil: 'anonyme' } },
};
if (AUTH) {
    // Une seule session (un utilisateur) : 1,5 itération/s × 3 requêtes = 270 requêtes/min, sous la limite de 300.
    // La rafale part après la fin des lectures, sur la même fenêtre glissante.
    scenarios.lectures = {
        executor: 'constant-arrival-rate', rate: 3, timeUnit: '2s', duration: '50s',
        preAllocatedVUs: 3, maxVUs: 6, exec: 'lectures', tags: { profil: 'lectures' },
    };
    scenarios.rafale = {
        executor: 'per-vu-iterations', vus: 1, iterations: 1, startTime: '52s', maxDuration: '40s',
        exec: 'rafale', tags: { profil: 'rafale' },
    };
}

export const options = {
    scenarios,
    thresholds: {
        // Aucune erreur hors codes attendus (401 anonymes, 429 de la rafale).
        http_req_failed: ['rate<0.01'],
        'http_req_duration{profil:anonyme}': ['p(95)<300'],
        'http_req_duration{profil:lectures}': ['p(95)<500'],
        checks: ['rate>0.99'],
        ...(AUTH ? {
            'http_req_failed{profil:lectures}': ['rate==0'],
            // La limite de débit doit se déclencher pendant la rafale.
            lettra_429_rafale: ['count>0'],
        } : {}),
    },
    summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

function authParams(extra = {}) {
    return {
        headers: { Cookie: COOKIE, 'X-Lettra-Csrf': '1', 'X-Dossier': DOSSIER, Accept: 'application/json' },
        responseCallback: ok,
        ...extra,
    };
}

// Fichiers de l'enveloppe : lus dans le manifeste du service worker (noms empreintés par la publication).
export function setup() {
    const health = http.get(`${BASE}/health`);
    if (health.status !== 200) fail(`Lettra ne répond pas : /health ${health.status}`);

    const manifest = http.get(`${BASE}/service-worker-assets.js`);
    const assets = [];
    if (manifest.status === 200) {
        const body = manifest.body;
        const parsed = JSON.parse(body.slice(body.indexOf('{'), body.lastIndexOf('}') + 1));
        const wanted = [/\.wasm$/, /^_framework\/dotnet[^/]*\.js$/, /\.css$/, /Lettra\.UI\.Shared[^/]*\.wasm$/];
        for (const pattern of wanted) {
            const found = parsed.assets.filter(a => pattern.test(a.url)).slice(0, 3);
            for (const a of found) if (!assets.includes(a.url)) assets.push(a.url);
        }
    }

    if (AUTH) {
        const user = http.get(`${BASE}/bff/user`, authParams());
        if (user.status !== 200) fail(`session refusée par /bff/user (${user.status}) : cookie expiré ou absent`);
    }

    return { assets: assets.slice(0, 8) };
}

export function anonyme(data) {
    group('santé', () => {
        const r = http.get(`${BASE}/health`, { responseCallback: ok });
        check(r, { 'santé 200': x => x.status === 200 });
    });

    group('enveloppe PWA', () => {
        const pages = ['/', '/manifest.webmanifest', '/service-worker.js', '/pwa.js', '/_framework/blazor.webassembly.js'];
        const responses = http.batch(pages.map(p => ['GET', `${BASE}${p}`, null, { responseCallback: ok, tags: { name: p } }]));
        check(responses[0], {
            'index 200': x => x.status === 200,
            'index avec CSP': x => (x.headers['Content-Security-Policy'] || '').includes("'wasm-unsafe-eval'"),
            'index sans cadre': x => x.headers['X-Frame-Options'] === 'DENY',
        });
        check(responses, { 'enveloppe complète (200)': rs => rs.every(x => x.status === 200) });
    });

    if (data.assets.length) {
        group('fichiers du framework', () => {
            const url = data.assets[Math.floor(Math.random() * data.assets.length)];
            const r = http.get(`${BASE}/${url}`, { responseCallback: ok, tags: { name: '/_framework/*' } });
            check(r, { 'fichier statique 200': x => x.status === 200 });
            assetBytes.add(r.body ? r.body.length : 0);
        });
    }

    group('API sans session', () => {
        const r = http.get(`${BASE}/api/v1/customers`, {
            headers: { 'X-Lettra-Csrf': '1', 'X-Dossier': DOSSIER },
            responseCallback: unauthorized,
            tags: { name: '/api/v1/customers (anonyme)' },
        });
        check(r, {
            'API anonyme 401': x => x.status === 401,
            'API jamais mise en cache': x => (x.headers['Cache-Control'] || '').includes('no-store'),
        });
    });

    sleep(1);
}

export function lectures() {
    const user = http.get(`${BASE}/bff/user`, authParams({ tags: { name: '/bff/user' } }));
    check(user, { 'session ouverte': x => x.status === 200 && x.json('isAuthenticated') === true });

    const customers = http.get(`${BASE}/api/v1/customers?page=1&pageSize=50`, authParams({ tags: { name: '/api/v1/customers' } }));
    check(customers, { 'clients 200': x => x.status === 200, 'clients listés': x => Array.isArray(x.json('items')) });

    const invoices = http.get(`${BASE}/api/v1/invoices?page=1&pageSize=50`, authParams({ tags: { name: '/api/v1/invoices' } }));
    check(invoices, { 'factures 200': x => x.status === 200 });
}

// Au-delà de la limite par utilisateur : 429 attendu, avec Retry-After et le code d'erreur stable.
export function rafale() {
    let first429 = null;
    for (let i = 0; i < 400 && first429 === null; i++) {
        const r = http.get(`${BASE}/api/v1/customers?pageSize=1`, authParams({ responseCallback: okOrLimited, tags: { name: '/api/v1/customers (rafale)' } }));
        if (r.status === 429) {
            first429 = r;
            limited.add(1);
        } else {
            check(r, { 'rafale : 200 avant la limite': x => x.status === 200 });
            admitted.add(1);
        }
    }

    check(first429, {
        'rafale : 429 atteint': x => x !== null,
        'rafale : Retry-After': x => x !== null && Number(x.headers['Retry-After']) >= 1,
        'rafale : code stable': x => x !== null && x.json('code') === 'Api.TooManyRequests',
    });
}

export function handleSummary(data) {
    return {
        stdout: textSummary(data, { indent: ' ', enableColors: true }),
        [`${OUT}/resume.txt`]: textSummary(data, { indent: ' ', enableColors: false }),
        [`${OUT}/resume.json`]: JSON.stringify(data, null, 2),
    };
}
