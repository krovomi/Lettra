// Parcours de la démonstration dans Chromium : connexion Keycloak (mot de passe + TOTP), écran des clients,
// création d'un client, manifeste et service worker de la PWA, rechargement hors ligne. Données fictives uniquement.
//
//   node parcours.mjs premier     enrôlement TOTP à la première connexion, puis seconde connexion (mot de passe + code)
//   node parcours.mjs suivant     connexion d'un utilisateur déjà enrôlé (secret lu dans $SORTIE/totp-secret.txt)
//   node parcours.mjs session     connexion (enrôlement si besoin) puis export du cookie de session pour k6
//                                 ($SORTIE/session-cookie.txt, en-tête Cookie prêt à l'emploi ; deploy/tests/k6)
//
// Variables : LETTRA_URL (http://localhost:8080), LETTRA_DEMO_USER (demo), LETTRA_DEMO_PASSWORD, SORTIE (./sortie).
import { createHmac } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { chromium } from 'playwright';

const mode = process.argv[2] ?? 'premier';
const baseUrl = (process.env.LETTRA_URL ?? 'http://localhost:8080').replace(/\/$/, '');
const user = process.env.LETTRA_DEMO_USER ?? 'demo';
const password = process.env.LETTRA_DEMO_PASSWORD ?? fail('LETTRA_DEMO_PASSWORD manquant');
const out = process.env.SORTIE ?? 'sortie';
const secretFile = join(out, 'totp-secret.txt');
mkdirSync(out, { recursive: true });

function fail(message) {
    throw new Error(message);
}

function log(message) {
    console.log(`[${new Date().toISOString()}] ${message}`);
}

// TOTP (RFC 6238) : HmacSHA256, 6 chiffres, 30 s, comme la politique OTP du realm.
function base32(text) {
    const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
    let bits = '';
    for (const c of text.replace(/[\s=]/g, '').toUpperCase()) {
        const v = alphabet.indexOf(c);
        if (v < 0) fail(`secret TOTP illisible : ${text}`);
        bits += v.toString(2).padStart(5, '0');
    }
    const bytes = [];
    for (let i = 0; i + 8 <= bits.length; i += 8) bytes.push(parseInt(bits.slice(i, i + 8), 2));
    return Buffer.from(bytes);
}

function totp(secret, step) {
    const counter = Buffer.alloc(8);
    counter.writeBigUInt64BE(BigInt(step));
    const h = createHmac('sha256', base32(secret)).update(counter).digest();
    const o = h[h.length - 1] & 0xf;
    return String((h.readUInt32BE(o) & 0x7fffffff) % 1_000_000).padStart(6, '0');
}

const currentStep = () => Math.floor(Date.now() / 30_000);
// Dernière période utilisée, gardée entre deux exécutions : deux connexions successives dans la même période de
// 30 s présenteraient le même code, que Keycloak refuse.
const stepFile = join(out, 'totp-periode.txt');
let lastStep = existsSync(stepFile) ? Number(readFileSync(stepFile, 'utf8')) : -1;

// Keycloak refuse un code déjà utilisé : on attend la période suivante si besoin, et pas en fin de période.
async function freshCode(secret) {
    for (;;) {
        const step = currentStep();
        const left = 30 - (Math.floor(Date.now() / 1000) % 30);
        if (step > lastStep && left > 5) {
            lastStep = step;
            writeFileSync(stepFile, String(step));
            return totp(secret, step);
        }
        await new Promise(r => setTimeout(r, 1000));
    }
}

async function shot(page, name) {
    await page.screenshot({ path: join(out, `${mode}-${name}.png`), fullPage: true });
}

const browser = await chromium.launch();

async function session(label, steps) {
    const context = await browser.newContext({ locale: 'fr-FR', serviceWorkers: 'allow' });
    await context.tracing.start({ screenshots: true, snapshots: true });
    const page = await context.newPage();
    page.on('console', m => log(`console ${m.type()} : ${m.text()}`));
    page.on('pageerror', e => log(`erreur de page : ${e.message}`));
    try {
        await steps(page);
    } catch (e) {
        await shot(page, `${label}-echec`).catch(() => {});
        log(`ÉCHEC (${label}) sur ${page.url()}`);
        throw e;
    } finally {
        await context.tracing.stop({ path: join(out, `${mode}-${label}-trace.zip`) });
        await context.close();
    }
}

// Ouvre Lettra, suit « Se connecter » jusqu'à Keycloak et saisit l'identifiant et le mot de passe.
async function signIn(page, label) {
    log(`${label} : ouverture de ${baseUrl}/`);
    await page.goto(`${baseUrl}/`);
    const signInLink = page.locator('a[data-sign-in]');
    await signInLink.waitFor({ timeout: 60_000 });
    await shot(page, `${label}-1-session`);
    await signInLink.click();
    await page.locator('#username').waitFor({ timeout: 30_000 });
    log(`${label} : page de connexion Keycloak ${new URL(page.url()).origin}`);
    await page.fill('#username', user);
    await page.fill('#password', password);
    await shot(page, `${label}-2-keycloak`);
    await page.click('#kc-login');
}

// Enrôlement TOTP imposé à la première connexion : clé affichée en mode manuel (« Impossible de scanner ? »).
async function enrol(page) {
    await page.locator('#kc-totp-secret-qr-code, #mode-manual, #kc-totp-secret-key').first().waitFor({ timeout: 30_000 });
    await shot(page, 'enrolement-1-qr');
    if (!(await page.locator('#kc-totp-secret-key').isVisible())) {
        await page.click('#mode-manual');
    }
    const secret = (await page.locator('#kc-totp-secret-key').innerText()).replace(/\s/g, '');
    writeFileSync(secretFile, secret);
    log(`enrôlement : clé TOTP lue (${secret.length} caractères)`);
    await shot(page, 'enrolement-2-cle');
    await page.fill('#totp', await freshCode(secret));
    if (await page.locator('#userLabel').count()) await page.fill('#userLabel', 'Démonstration');
    await page.click('#saveTOTPBtn, input[type=submit], button[type=submit]');
    return secret;
}

// Après l'enrôlement, Lettra demande une nouvelle authentification complète (le jeton de l'enrôlement ne prouve
// pas le second facteur) : Keycloak redemande mot de passe puis code.
async function reauthenticate(page, secret) {
    const next = await Promise.race([
        page.locator('#password').waitFor({ timeout: 30_000 }).then(() => 'keycloak', () => 'aucun'),
        page.waitForURL(u => u.href.startsWith(baseUrl) && !u.pathname.startsWith('/bff/'), { timeout: 30_000 }).then(() => 'lettra', () => 'aucun'),
    ]);
    if (next !== 'keycloak') return;
    log('nouvelle authentification demandée après l\'enrôlement');
    await shot(page, 'enrolement-3-reconnexion');
    // Keycloak garde l'identifiant de la première étape (champ #username absent ou en lecture seule).
    if (await page.locator('#username').isEditable().catch(() => false)) await page.fill('#username', user);
    await page.fill('#password', password);
    await page.click('#kc-login');
    await secondFactor(page, secret);
}

async function secondFactor(page, secret) {
    await page.locator('#otp').waitFor({ timeout: 30_000 });
    await shot(page, 'connexion-otp');
    await page.fill('#otp', await freshCode(secret));
    await page.click('#kc-login');
}

// De retour dans Lettra : la session est ouverte et l'écran des clients s'affiche.
async function landed(page, label) {
    await page.waitForURL(u => u.href.startsWith(baseUrl) && !u.pathname.startsWith('/bff/'), { timeout: 60_000 });
    if (new URL(page.url()).pathname === '/session') {
        // L'écran de session redirige vers l'accueil une fois connecté.
        await page.locator('.lettra-user, a[data-sign-in], .validation-message').first().waitFor({ timeout: 30_000 });
    }
    await page.locator('.lettra-user').waitFor({ timeout: 60_000 });
    log(`${label} : connecté comme « ${await page.locator('.lettra-user').innerText()} »`);
    await page.goto(`${baseUrl}/customers`);
    await page.locator('#list-title').waitFor({ timeout: 60_000 });
    await page.locator('#customers-list, #customers-empty').first().waitFor({ timeout: 60_000 });
    await shot(page, `${label}-3-clients`);
}

// Retrouve un client par la recherche de la page Clients (la liste est paginée).
async function findCustomer(page, name) {
    await page.fill('#customers-search', name);
    await page.locator('#customers-list [role=option]', { hasText: name }).first().waitFor({ timeout: 30_000 });
}

async function createCustomer(page, name) {
    await page.locator('#customers-new').click();
    await page.locator('#name').waitFor({ timeout: 30_000 });
    await page.locator('label', { hasText: 'Particulier' }).locator('input[type=radio]').check().catch(async () => {
        await page.locator('input[type=radio]').nth(1).check();
    });
    await page.fill('#name', name);
    await page.fill('#email', 'contact@client-demo.example');
    await page.fill('#line1', '1 rue de la Démonstration');
    await page.fill('#zip', '75001');
    await page.fill('#city', 'Paris');
    await page.fill('#country', 'FR');
    await page.locator('form button[type=submit]').click();
    await page.locator('.lettra-success').waitFor({ timeout: 30_000 });
    await page.locator('#customer-sheet-title', { hasText: name }).waitFor({ timeout: 30_000 });
    await findCustomer(page, name);
    log(`client « ${name} » créé et listé`);
    await shot(page, 'client-cree');
}

// PWA : manifeste servi, service worker actif, et aucune réponse de l'API ou du BFF dans ses caches.
async function checkPwa(page) {
    const manifest = await page.evaluate(async () => {
        const link = document.querySelector('link[rel=manifest]');
        const response = await fetch(link.href);
        return { type: response.headers.get('content-type'), body: await response.json() };
    });
    if (manifest.body.name !== 'Lettra' || manifest.body.display !== 'standalone') fail(`manifeste inattendu : ${JSON.stringify(manifest)}`);
    log(`manifeste : ${manifest.type}, ${manifest.body.icons.length} icônes`);
    const sw = await page.evaluate(async () => {
        const registration = await navigator.serviceWorker.ready;
        return registration.active?.scriptURL;
    });
    if (!sw) fail('service worker inactif');
    log(`service worker actif : ${sw}`);
    // Critères d'installation de Chromium (manifeste, icônes, contexte sécurisé, service worker).
    const cdp = await page.context().newCDPSession(page);
    const { installabilityErrors } = await cdp.send('Page.getInstallabilityErrors');
    if (installabilityErrors.length) fail(`PWA non installable : ${JSON.stringify(installabilityErrors)}`);
    log('PWA installable selon Chromium');
    // Recharge pour que les requêtes passent par le service worker, puis inspecte ses caches.
    await page.reload();
    await page.locator('#list-title').waitFor({ timeout: 60_000 });
    const cached = await page.evaluate(async () => {
        const urls = [];
        for (const key of await caches.keys()) {
            const cache = await caches.open(key);
            urls.push(...(await cache.keys()).map(r => r.url));
        }
        return urls;
    });
    const leaked = cached.filter(u => /\/(api|bff)\//.test(new URL(u).pathname));
    if (leaked.length) fail(`le service worker a mis en cache des données : ${leaked.join(', ')}`);
    if (!cached.some(u => u.endsWith('/index.html'))) fail(`enveloppe absente du cache (${cached.length} entrées)`);
    log(`cache du service worker : ${cached.length} fichiers statiques, aucun /api/ ni /bff/`);
    await checkOffline(page, cached);
}

// Hors ligne (étape 4.5) : l'application se recharge depuis le cache du service worker, et le module des brouillons
// écrit et relit IndexedDB sans réseau. L'envoi des brouillons au retour du réseau est couvert par les tests bUnit.
async function checkOffline(page, cached) {
    const draftsScript = `${baseUrl}/_content/Lettra.UI.Shared/lettra-drafts.js`;
    if (!cached.includes(draftsScript)) fail(`module des brouillons absent du cache : ${draftsScript}`);
    await page.context().setOffline(true);
    try {
        await page.reload();
        await page.locator('#list-title, a[data-sign-in], .lettra-user').first().waitFor({ timeout: 60_000 });
        await shot(page, 'hors-ligne');
        const kept = await page.evaluate(async url => {
            const drafts = await import(url);
            await drafts.putDraft('parcours-e2e', JSON.stringify({ essai: 'parcours-e2e' }));
            const listed = await drafts.listDrafts();
            await drafts.deleteDraft('parcours-e2e');
            return { online: drafts.isOnline(), listed: JSON.stringify(listed).includes('parcours-e2e') };
        }, draftsScript);
        if (kept.online || !kept.listed) fail(`brouillons hors ligne inattendus : ${JSON.stringify(kept)}`);
        log('hors ligne : application rechargée depuis le cache, brouillon écrit puis relu dans IndexedDB');
    } finally {
        await page.context().setOffline(false);
    }
}

try {
    if (mode === 'premier') {
        const name = `Client démo ${Date.now()}`;
        let secret;
        await session('premiere-connexion', async page => {
            await signIn(page, 'premiere-connexion');
            secret = await enrol(page);
            await reauthenticate(page, secret);
            await landed(page, 'premiere-connexion');
            await createCustomer(page, name);
            await checkPwa(page);
        });
        await session('seconde-connexion', async page => {
            await signIn(page, 'seconde-connexion');
            await secondFactor(page, secret);
            await landed(page, 'seconde-connexion');
            await findCustomer(page, name);
            log(`seconde connexion : le client « ${name} » est toujours là`);
        });
        writeFileSync(join(out, 'client.txt'), name);
    } else if (mode === 'suivant') {
        const secret = readFileSync(secretFile, 'utf8').trim();
        const name = readFileSync(join(out, 'client.txt'), 'utf8').trim();
        await session('connexion', async page => {
            await signIn(page, 'connexion');
            await secondFactor(page, secret);
            await landed(page, 'connexion');
            await findCustomer(page, name);
            log(`après redémarrage : le client « ${name} » est toujours là`);
            await checkPwa(page);
        });
    } else if (mode === 'session') {
        // Session du BFF pour le test de charge : connexion complète (mot de passe + TOTP, MFA prouvée comme pour
        // tout utilisateur), puis export du cookie __Host-lettra. Aucun réglage de Keycloak n'est assoupli.
        await session('session-k6', async page => {
            await signIn(page, 'session-k6');
            if (existsSync(secretFile)) {
                await secondFactor(page, readFileSync(secretFile, 'utf8').trim());
            } else {
                await reauthenticate(page, await enrol(page));
            }
            await landed(page, 'session-k6');
            await createCustomer(page, `Client charge ${Date.now()}`);
            const cookies = await page.context().cookies(baseUrl);
            if (!cookies.some(c => c.name === '__Host-lettra')) fail('cookie de session __Host-lettra absent');
            writeFileSync(join(out, 'session-cookie.txt'), cookies.map(c => `${c.name}=${c.value}`).join('; '), { mode: 0o600 });
            log(`session exportée : ${cookies.length} cookie(s) pour ${baseUrl}`);
        });
    } else {
        fail(`mode inconnu : ${mode}`);
    }
    log('parcours réussi');
} finally {
    await browser.close();
}
