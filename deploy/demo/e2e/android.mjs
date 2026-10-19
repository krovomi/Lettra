// Serveur commun sur Android (docs/serveur-commun.md) : l'application installée, dans un émulateur relié à la démo par
// « adb reverse », est reliée au serveur du cabinet par l'écran « Serveur du cabinet », se connecte (onglet du navigateur,
// mot de passe + code), voit le client saisi dans le navigateur et en crée un que le navigateur retrouvera
// (node appareil.mjs voir). L'interface est pilotée par l'accessibilité d'Android (Playwright _android).
//   node android.mjs <client du navigateur>
// Variables : LETTRA_URL (http://localhost:8080), LETTRA_DEMO_USER, LETTRA_DEMO_PASSWORD, SORTIE (./sortie).
// Écrit SORTIE/client-appareil.txt et des captures dans SORTIE/android/.
import { createHmac } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { join } from 'node:path';
import { _android as android, chromium } from 'playwright';

const [browserCustomer] = process.argv.slice(2);
const out = process.env.SORTIE ?? 'sortie';
const shots = join(out, 'android');
mkdirSync(shots, { recursive: true });
const baseUrl = (process.env.LETTRA_URL ?? 'http://localhost:8080').replace(/\/$/, '');
const user = process.env.LETTRA_DEMO_USER ?? 'demo';
const password = process.env.LETTRA_DEMO_PASSWORD ?? 'demo-lettra-demo';
const secret = readFileSync(join(out, 'totp-secret.txt'), 'utf8').trim();
const stepFile = join(out, 'totp-periode.txt');
const app = 'fr.lettra.app';
const log = m => console.log(`[android ${new Date().toISOString()}] ${m}`);

function base32(text) {
    const a = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'; let bits = '';
    for (const c of text.replace(/[\s=]/g, '').toUpperCase()) bits += a.indexOf(c).toString(2).padStart(5, '0');
    const bytes = []; for (let i = 0; i + 8 <= bits.length; i += 8) bytes.push(parseInt(bits.slice(i, i + 8), 2));
    return Buffer.from(bytes);
}
function totp(step) {
    const counter = Buffer.alloc(8); counter.writeBigUInt64BE(BigInt(step));
    const h = createHmac('sha256', base32(secret)).update(counter).digest(); const o = h[h.length - 1] & 0xf;
    return String((h.readUInt32BE(o) & 0x7fffffff) % 1_000_000).padStart(6, '0');
}
async function freshCode() {
    const last = existsSync(stepFile) ? Number(readFileSync(stepFile, 'utf8')) : -1;
    for (;;) {
        const step = Math.floor(Date.now() / 30_000); const left = 30 - (Math.floor(Date.now() / 1000) % 30);
        if (step > last && left > 5) { writeFileSync(stepFile, String(step)); return totp(step); }
        await new Promise(r => setTimeout(r, 1000));
    }
}

const [device] = await android.devices();
if (!device) throw new Error('Aucun appareil Android (adb devices).');
const escape = text => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
const sleep = ms => new Promise(r => setTimeout(r, ms));
let shotIndex = 0;
async function shot(name) {
    const file = join(shots, `${String(++shotIndex).padStart(2, '0')}-${name}.png`);
    await device.screenshot({ path: file }).catch(() => {});
}
// L'identifiant HTML d'un élément de la page (WebView) ou de Keycloak (Chrome) devient son identifiant d'accessibilité.
const id = name => ({ res: new RegExp(`(^|[:/])${name}$`) });
async function visible(selector, timeout) {
    try { await device.wait(selector, { timeout }); return true; } catch (error) {
        if (!/timeout/i.test(String(error?.message))) log(`attente interrompue : ${error?.message}`);
        return false;
    }
}
async function waitFor(selector, what, timeout = 60_000) {
    if (!(await visible(selector, timeout))) {
        await shot(`echec-${what}`);
        // « uiautomator dump » n'est pas possible pendant que Playwright pilote l'appareil : la fenêtre au premier plan suffit.
        const focus = await device.shell('dumpsys window | grep -E "mCurrentFocus|mFocusedApp"').then(b => b.toString()).catch(e => String(e));
        log(`premier plan :\n${focus.trim()}`);
        throw new Error(`${what} : introuvable après ${timeout / 1000} s (capture dans ${shots}).`);
    }
}
// Saisie par l'accessibilité (sans clavier à l'écran, qui masquerait les boutons).
async function type(selector, text) {
    await device.fill(selector, text);
}
// Onglet Chrome ouvert par l'application sur la page de connexion de Keycloak (/realms/…).
async function keycloakPage(timeout) {
    const deadline = Date.now() + timeout;
    let browser;
    while (Date.now() < deadline) {
        try {
            browser ??= await chromium.connectOverCDP('http://127.0.0.1:9333', { timeout: 5_000 });
            for (const page of browser.contexts().flatMap(c => c.pages()))
                if (/\/realms\//.test(page.url()) && await page.locator('#username').count()) return page;
        } catch (error) {
            browser = undefined;
            if (Date.now() + 2_000 > deadline) log(`Chrome : ${error.message.split('\n')[0]}`);
        }
        await sleep(2_000);
    }
    await shot('echec-keycloak');
    for (const command of ['getprop ro.debuggable', 'cat /data/local/tmp/chrome-command-line', 'grep -i devtools /proc/net/unix',
        'dumpsys window | grep -E "mCurrentFocus|mFocusedApp"'])
        log(`${command} :\n${(await device.shell(command).then(b => b.toString()).catch(e => String(e))).trim()}`);
    if (browser) for (const page of browser.contexts().flatMap(c => c.pages())) await dumpPage(page);
    throw new Error(`page de connexion Keycloak : introuvable après ${timeout / 1000} s dans Chrome.`);
}
async function dumpPage(page) {
    const text = await page.locator('body').innerText({ timeout: 2_000 }).catch(() => '');
    log(`onglet ${page.url()} :\n${text.slice(0, 1500)}`);
}
async function start() {
    await device.shell(`am start -W -n ${app}/.MainActivity`);
}

// 1. Premier lancement : l'application travaille seule ; l'écran de session mène au choix du serveur.
await device.shell(`am force-stop ${app}`);
await start();
await waitFor(id('shared-server-link'), 'lien « Serveur du cabinet » de l\'écran de session', 120_000);
await shot('session-appareil-seul');
await device.tap(id('shared-server-link'));
await waitFor(id('shared-server-address'), 'champ de l\'adresse du serveur');
await type(id('shared-server-address'), baseUrl);
await shot('adresse-saisie');
await device.tap(id('shared-server-submit'));
await waitFor(id('shared-server-restart-now'), 'serveur vérifié et retenu');
await shot('serveur-retenu');
log(`serveur ${baseUrl} retenu`);
await device.tap(id('shared-server-restart-now'));
await sleep(3000);

// 2. Relancée, l'application est reliée au serveur : connexion du cabinet dans un onglet du navigateur.
await device.shell(`am force-stop ${app}`);
await start();
await waitFor({ text: /^(Sign in|Se connecter)$/ }, 'bouton de connexion du cabinet', 120_000);
await shot('session-serveur');
// La page de connexion s'ouvre dans un onglet de Chrome : Playwright s'y attache par le protocole de débogage (sans écran
// d'accueil de Chrome, options lues au démarrage dans /data/local/tmp/chrome-command-line, comme device.launchBrowser).
const devtools = 'lettra_devtools_remote';
await device.shell('am force-stop com.android.chrome');
await device.shell(`echo "_ --disable-fre --no-default-browser-check --no-first-run --remote-debugging-socket-name=${devtools}" > /data/local/tmp/chrome-command-line`);
// Sur une image non « debuggable », Chrome ne lit ce fichier que s'il est l'application de débogage.
await device.shell('am set-debug-app --persistent com.android.chrome');
execFileSync('adb', ['forward', 'tcp:9333', `localabstract:${devtools}`]);
await device.tap({ text: /^(Sign in|Se connecter)$/ });
const keycloak = await keycloakPage(90_000);
await shot('keycloak');
await keycloak.fill('#username', user);
await keycloak.fill('#password', password);
await keycloak.click('#kc-login');
await keycloak.waitForSelector('#otp', { timeout: 30_000 }).catch(async error => { await dumpPage(keycloak); throw error; });
await keycloak.fill('#otp', await freshCode());
await keycloak.click('#kc-login');
log('connexion du cabinet envoyée (mot de passe + code)');

// 3. De retour dans l'application : le client saisi dans le navigateur est là.
await waitFor({ text: /^(Customers|Clients)$/ }, 'accueil de l\'application connectée', 120_000);
await shot('connecte');
await device.tap({ text: /^(Customers|Clients)$/ });
await waitFor(id('customers-search'), 'page Clients');
await type(id('customers-search'), browserCustomer);
await waitFor({ text: new RegExp(escape(browserCustomer)) }, `client « ${browserCustomer} » du navigateur`, 30_000);
await shot('client-du-navigateur');
log(`client du navigateur « ${browserCustomer} » vu sur l'appareil`);

// 4. Un client saisi sur l'appareil, que le navigateur retrouvera.
const name = `Client Android ${new Date().toISOString().replace(/\D/g, '').slice(0, 14)}`;
await device.tap(id('customers-new'));
await waitFor(id('name'), 'formulaire du client');
if (await visible({ text: /^(Individual|Particulier)$/ }, 3_000)) await device.tap({ text: /^(Individual|Particulier)$/ });
await type(id('name'), name);
await type(id('email'), 'android@client-demo.example');
await type(id('line1'), '2 rue de l\'Appareil');
await type(id('zip'), '75002');
await type(id('city'), 'Paris');
await type(id('country'), 'FR');
await shot('client-saisi');
await device.tap(id('explorer-save'));
// Confirmation du toaster (« Client « … » enregistré. ») : le nom seul serait déjà dans le champ.
await waitFor({ text: new RegExp(`${escape(name)}.*(saved|enregistré)`) }, `client « ${name} » enregistré`, 30_000);
await shot('client-cree');
writeFileSync(join(out, 'client-appareil.txt'), name);
log(`client « ${name} » créé sur l'appareil`);
await device.close();
process.exit(0);
