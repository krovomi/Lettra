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
//
// Profil « activité de démonstration » (LETTRA_PROFIL=activite) : remplit les tableaux de bord Grafana de la démonstration
// (deploy/demo/compose.observabilite.yaml). Pendant LETTRA_DUREE (10m par défaut), quelques utilisateurs virtuels
// (LETTRA_UTILISATEURS, 3) partagent la session de démonstration et enchaînent des actions tirées au hasard, au rythme
// de LETTRA_RYTHME actions par minute (24) : ventes complètes (devis, validation, acceptation, facture, validation,
// transmission à la plateforme simulée, parfois un avoir), devis refusés, consultations, synchronisation, questions à
// l'assistant (désactivé : refus attendu), exports FEC. Un visiteur anonyme charge l'application, et un retour de
// connexion invalide par minute alimente le compteur des connexions refusées. Données fictives uniquement ; la session
// est obligatoire. Le rythme reste sous la limite de débit par utilisateur.
//
//   k6 run -e LETTRA_PROFIL=activite -e LETTRA_DUREE=10m -e LETTRA_SESSION_COOKIE="$(cat session-cookie.txt)" deploy/tests/k6/fumee.js
import http from 'k6/http';
import exec from 'k6/execution';
import { check, fail, group, sleep } from 'k6';
import { Counter, Trend } from 'k6/metrics';
import { textSummary } from 'https://jslib.k6.io/k6-summary/0.1.0/index.js';

const BASE = (__ENV.LETTRA_URL || 'http://localhost:8080').replace(/\/$/, '');
const COOKIE = (__ENV.LETTRA_SESSION_COOKIE || '').trim();
// Dossier de démonstration (deploy/demo/compose.yaml, Lettra__Dossiers__0__Id).
const DOSSIER = __ENV.LETTRA_DOSSIER || '0199e3a0-0000-7000-8000-00000000d051';
const OUT = __ENV.K6_SORTIE || '.';
const AUTH = COOKIE !== '';
const ACTIVITE = (__ENV.LETTRA_PROFIL || 'fumee').toLowerCase() === 'activite';
const DUREE = __ENV.LETTRA_DUREE || '10m';
const UTILISATEURS = Math.max(1, Number(__ENV.LETTRA_UTILISATEURS || 3));
const RYTHME = Math.max(1, Number(__ENV.LETTRA_RYTHME || 24));

const sales = new Counter('lettra_ventes');
const limited = new Counter('lettra_429_rafale');
const admitted = new Counter('lettra_admises_rafale');
const assetBytes = new Trend('lettra_taille_fichier_statique', false);

// Codes attendus par requête : un 401 sans session et un 429 au-delà de la limite sont le comportement voulu.
const ok = http.expectedStatuses(200);
const unauthorized = http.expectedStatuses(401);
const okOrLimited = http.expectedStatuses(200, 429);
const created = http.expectedStatuses(201);
const redirected = http.expectedStatuses(302);

const scenarios = ACTIVITE ? {
    // Profil « activité » : un visiteur anonyme, les utilisateurs du cabinet, des retours de connexion refusés.
    visiteur: { executor: 'constant-vus', vus: 1, duration: DUREE, exec: 'anonyme', tags: { profil: 'anonyme' } },
    cabinet: {
        executor: 'constant-arrival-rate', rate: RYTHME, timeUnit: '1m', duration: DUREE,
        preAllocatedVUs: UTILISATEURS, maxVUs: UTILISATEURS * 2, exec: 'activite', tags: { profil: 'activite' },
    },
    connexions_refusees: {
        executor: 'constant-arrival-rate', rate: 1, timeUnit: '1m', duration: DUREE,
        preAllocatedVUs: 1, maxVUs: 1, exec: 'connexionRefusee', tags: { profil: 'connexion' },
    },
} : {
    anonyme: { executor: 'constant-vus', vus: 4, duration: '50s', exec: 'anonyme', tags: { profil: 'anonyme' } },
};
if (AUTH && !ACTIVITE) {
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
    thresholds: ACTIVITE ? {
        // L'activité doit tourner sans erreur inattendue et produire au moins une vente complète.
        http_req_failed: ['rate<0.02'],
        checks: ['rate>0.95'],
        lettra_ventes: ['count>0'],
    } : {
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
        if (user.status !== 200 || user.json('isAuthenticated') !== true) fail(`session refusée par /bff/user (${user.status}) : cookie expiré ou absent`);
    }

    if (ACTIVITE && !AUTH) fail('profil activite : LETTRA_SESSION_COOKIE est obligatoire (node deploy/demo/e2e/parcours.mjs session)');
    return { assets: assets.slice(0, 8), ...(ACTIVITE ? preparerCabinet() : {}) };
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

// --- Profil « activité de démonstration » -------------------------------------------------------------------------
// Données fictives, reprises du parcours « Premier usage » du README (DemoWalkthroughTests).

const API = `${BASE}/api/v1`;
const ANNEE = new Date().getUTCFullYear();

function writeParams(version, expected = ok, name = undefined) {
    const params = authParams({ responseCallback: expected, tags: name ? { name } : {} });
    params.headers['Content-Type'] = 'application/json';
    if (version !== undefined) params.headers['If-Match'] = version === '*' ? '*' : `"${version}"`;
    return params;
}

function post(path, body, name, expected = ok, version = undefined) {
    return http.post(`${API}${path}`, body === null ? null : JSON.stringify(body), writeParams(version, expected, name));
}

function put(path, body, name, version) {
    return http.put(`${API}${path}`, JSON.stringify(body), writeParams(version, ok, name));
}

function get(path, name, expected = ok) {
    return http.get(`${API}${path}`, authParams({ responseCallback: expected, tags: { name } }));
}

// Fiche entreprise (nécessaire à la validation) et deux clients fictifs : une entreprise (facture électronique) et un
// particulier (e-reporting). Rien n'est recréé s'ils existent déjà.
function preparerCabinet() {
    const profile = get('/company-profile', '/api/v1/company-profile', http.expectedStatuses(200, 404));
    if (profile.status === 404) {
        const saved = put('/company-profile', {
            name: 'Cabinet Démo Conseil', legalForm: 'SAS', shareCapital: 10000, siren: '900000019',
            rcs: 'RCS Paris 900 000 019', vatNumber: 'FR25900000019',
            addressLine1: '12 rue des Exemples', postalCode: '75010', city: 'Paris',
            latePenaltyRate: 10, earlyPaymentDiscount: "Pas d'escompte pour paiement anticipé",
        }, '/api/v1/company-profile', '*');
        if (saved.status !== 200) fail(`fiche entreprise refusée (${saved.status}) : ${saved.body}`);
    }

    const fictifs = [
        { kind: 'Company', name: 'Boulangerie Exemple', siret: '90000002700012', addressLine1: '3 place du Marché', postalCode: '69002', city: 'Lyon' },
        { kind: 'Individual', name: 'Camille Exemple', addressLine1: '8 allée des Essais', postalCode: '33000', city: 'Bordeaux' },
    ];
    const clients = fictifs.map(client => {
        const found = get(`/customers?search=${encodeURIComponent(client.name)}&pageSize=1`, '/api/v1/customers');
        const existing = found.status === 200 ? (found.json('items') || [])[0] : undefined;
        if (existing) return existing.id;
        const made = post('/customers', client, '/api/v1/customers', created);
        if (made.status !== 201) fail(`client fictif refusé (${made.status}) : ${made.body}`);
        return made.json('id');
    });
    return { clients };
}

const PRESTATIONS = [
    ['Tenue comptable mensuelle', 250], ['Bilan annuel', 1200], ['Déclaration de TVA', 90],
    ['Accompagnement création', 600], ['Paie (par bulletin)', 35],
];

function nouveauDevis(data) {
    const [designation, prix] = PRESTATIONS[Math.floor(Math.random() * PRESTATIONS.length)];
    const quote = post('/quotes', {
        customerId: data.clients[Math.floor(Math.random() * data.clients.length)],
        subject: 'Mission de démonstration',
        lines: [{ designation, quantity: 1 + Math.floor(Math.random() * 3), unitPriceExclTax: prix, unitCode: 'C62', kind: 'Service' }],
    }, '/api/v1/quotes', created);
    if (!check(quote, { 'devis créé': r => r.status === 201 })) return null;
    const validated = post(`/quotes/${quote.json('id')}/validate`, null, '/api/v1/quotes/{id}/validate', ok, quote.json('version'));
    return check(validated, { 'devis validé': r => r.status === 200 }) ? validated : null;
}

function venteComplete(data, avecAvoir = Math.random() < 0.3) {
    const quote = nouveauDevis(data);
    if (!quote) return;
    const id = quote.json('id');
    const accepted = put(`/quotes/${id}/outcome`, { accepted: true }, '/api/v1/quotes/{id}/outcome', quote.json('version'));
    if (!check(accepted, { 'devis accepté': r => r.status === 200 })) return;

    const draft = post(`/quotes/${id}/invoice`, null, '/api/v1/quotes/{id}/invoice', created);
    if (!check(draft, { 'facture créée': r => r.status === 201 })) return;
    const invoice = post(`/invoices/${draft.json('id')}/validate`, null, '/api/v1/invoices/{id}/validate', ok, draft.json('version'));
    if (!check(invoice, { 'facture validée': r => r.status === 200 })) return;

    const sent = post(`/invoices/${invoice.json('id')}/platform-transmission`, { format: 'Cii' }, '/api/v1/invoices/{id}/platform-transmission');
    if (!check(sent, { 'facture transmise': r => r.status === 200 })) return;
    sales.add(1);

    if (avecAvoir) {
        const credit = post(`/invoices/${invoice.json('id')}/credit-notes`, { reason: 'Geste commercial (démonstration)' }, '/api/v1/invoices/{id}/credit-notes', created);
        if (!check(credit, { 'avoir créé': r => r.status === 201 })) return;
        const validated = post(`/invoices/${credit.json('id')}/validate`, null, '/api/v1/invoices/{id}/validate', ok, credit.json('version'));
        check(validated, { 'avoir validé': r => r.status === 200 });
    }
}

function devisRefuse(data) {
    const quote = nouveauDevis(data);
    if (!quote) return;
    const refused = put(`/quotes/${quote.json('id')}/outcome`, { accepted: false }, '/api/v1/quotes/{id}/outcome', quote.json('version'));
    check(refused, { 'devis refusé par le client': r => r.status === 200 });
}

function consultation() {
    const pages = ['/customers?page=1&pageSize=20', '/quotes?page=1&pageSize=20', '/invoices?page=1&pageSize=20'];
    const names = ['/api/v1/customers', '/api/v1/quotes', '/api/v1/invoices'];
    const responses = pages.map((p, i) => get(p, names[i]));
    check(responses, { 'listes 200': rs => rs.every(r => r.status === 200) });
    const items = responses[2].status === 200 ? responses[2].json('items') || [] : [];
    if (items.length) {
        const one = get(`/invoices/${items[Math.floor(Math.random() * items.length)].id}`, '/api/v1/invoices/{id}');
        check(one, { 'facture lue': r => r.status === 200 });
    }
}

function synchronisation() {
    check(get('/sync/changes?limit=50', '/api/v1/sync/changes'), { 'synchronisation reçue': r => r.status === 200 });
}

function assistant() {
    // Assistant désactivé dans la démonstration : le refus (403 Ai.Disabled) est le comportement attendu.
    const r = post('/assistant', { feature: 'RegulatoryQuestions', question: 'Quel taux de TVA pour une prestation de conseil ?' }, '/api/v1/assistant', http.expectedStatuses(200, 403));
    check(r, { 'assistant : réponse ou refus attendu': x => x.status === 200 || x.status === 403 });
}

function exportFec() {
    const fec = get(`/exports/fec?from=${ANNEE}-01-01&to=${ANNEE}-12-31`, '/api/v1/exports/fec', http.expectedStatuses(200, 404));
    check(fec, { 'FEC exporté (ou rien à exporter)': r => r.status === 200 || r.status === 404 });
}

// Les premières actions passent chacune une fois par tous les cas (chaque courbe a un point, même sur un essai court),
// les suivantes sont tirées au hasard : surtout des ventes et des consultations.
const PREMIERES = [data => venteComplete(data, true), devisRefuse, consultation, synchronisation, assistant, exportFec];
const TIRAGE = [[0.45, venteComplete], [0.55, devisRefuse], [0.80, consultation], [0.88, synchronisation], [0.95, assistant], [1, exportFec]];

export function activite(data) {
    const rang = exec.scenario.iterationInTest;
    if (rang < PREMIERES.length) {
        PREMIERES[rang](data);
        return;
    }

    const tirage = Math.random();
    TIRAGE.find(([seuil]) => tirage < seuil)[1](data);
}

// Retour de Keycloak invalide (état inconnu) : le BFF le refuse et renvoie vers l'écran de session. Simule une
// connexion échouée, sans compte ni mot de passe.
export function connexionRefusee() {
    const r = http.get(`${BASE}/bff/signin-oidc?code=demo&state=etat-invalide`, { redirects: 0, responseCallback: redirected, tags: { name: '/bff/signin-oidc (refus)' } });
    check(r, { 'connexion refusée et renvoyée vers la session': x => x.status === 302 && (x.headers.Location || '').includes('erreur=connexion') });
}

// En CI : une annotation récapitulative (mesures des seuils), lisible sans télécharger les rapports.
function annotation(data) {
    const m = data.metrics;
    const v = (name, stat) => (m[name] && m[name].values[stat] !== undefined ? m[name].values[stat] : null);
    const ms = x => (x === null ? 'n/a' : `${x.toFixed(1)} ms`);
    const pct = x => (x === null ? 'n/a' : `${(x * 100).toFixed(2)} %`);
    if (ACTIVITE) {
        const activity = [
            `requêtes : ${v('http_reqs', 'count')} (${(v('http_reqs', 'rate') || 0).toFixed(1)}/s)`,
            `ventes complètes : ${v('lettra_ventes', 'count')}`,
            `échecs : ${pct(v('http_req_failed', 'rate'))} ; checks : ${pct(v('checks', 'rate'))}`,
        ];
        return __ENV.GITHUB_ACTIONS ? `::notice title=k6 (activité)::${activity.join('%0A')}\n` : '';
    }
    const lines = [
        `requêtes : ${v('http_reqs', 'count')} (${(v('http_reqs', 'rate') || 0).toFixed(1)}/s)`,
        `p95 anonyme : ${ms(v('http_req_duration{profil:anonyme}', 'p(95)'))}`,
        `p95 lectures : ${ms(v('http_req_duration{profil:lectures}', 'p(95)'))}`,
        `échecs : ${pct(v('http_req_failed', 'rate'))} ; lectures : ${pct(v('http_req_failed{profil:lectures}', 'rate'))}`,
        `rafale : ${v('lettra_admises_rafale', 'count')} admises puis ${v('lettra_429_rafale', 'count')} × 429`,
        `checks : ${pct(v('checks', 'rate'))}`,
    ];
    return __ENV.GITHUB_ACTIONS ? `::notice title=k6 (fumée)::${lines.join('%0A')}\n` : '';
}

export function handleSummary(data) {
    return {
        stdout: textSummary(data, { indent: ' ', enableColors: true }) + '\n' + annotation(data),
        [`${OUT}/resume.txt`]: textSummary(data, { indent: ' ', enableColors: false }),
        [`${OUT}/resume.json`]: JSON.stringify(data, null, 2),
    };
}
