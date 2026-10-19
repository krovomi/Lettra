// Serveur commun sur Android (docs/serveur-commun.md) : l'application installée, dans un émulateur relié à la démo par
// « adb reverse », est reliée au serveur du cabinet par l'écran « Serveur du cabinet », se connecte (onglet du navigateur,
// mot de passe + code), voit le client saisi dans le navigateur et en crée un que le navigateur retrouvera
// (node appareil.mjs voir). La page de l'application (WebView) et l'onglet de Chrome sont pilotés par leur port de débogage
// (Playwright _android et connectOverCDP).
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
const sleep = ms => new Promise(r => setTimeout(r, ms));
let shotIndex = 0;
async function shot(name) {
    const file = join(shots, `${String(++shotIndex).padStart(2, '0')}-${name}.png`);
    await device.screenshot({ path: file }).catch(() => {});
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
// Page Blazor de l'application (WebView, débogable) : pilotée comme une page du navigateur, par sélecteur CSS.
async function appPage() {
    const webview = await device.webView({ pkg: app }, { timeout: 120_000 });
    const page = await webview.page();
    page.setDefaultTimeout(60_000);
    return page;
}
// Clic au centre de l'écran : sur un téléphone, la barre de navigation fixe du bas recouvrirait un lien en bas de page.
// Si la WebView ne dessine plus d'images (écran éteint, requestAnimationFrame à l'arrêt), Playwright attend sans fin un
// élément « stable » : le clic part alors directement par le DOM, comme le ferait le doigt.
// La WebView n'est pas un navigateur piloté (pas de navigator.webdriver) : la visite guidée démarre seule, comme pour
// un vrai utilisateur, et son fond assombri capte les clics. On la passe comme lui, par « Passer ».
async function skipTour(page) {
    for (let i = 0; i < 5; i++) {
        const skip = page.locator('#tour-skip');
        if (!(await skip.isVisible().catch(() => false))) return;
        log('visite guidée passée');
        await skip.evaluate(element => element.click()).catch(() => {});
        await page.locator('.lt-tour').waitFor({ state: 'detached', timeout: 5_000 }).catch(() => {});
    }
}
async function press(locator, options) {
    await locator.waitFor(options);
    await skipTour(locator.page());
    await locator.evaluate(element => element.scrollIntoView({ block: 'center' }));
    try {
        await locator.click({ timeout: 10_000 });
    } catch (error) {
        log(`clic direct (${error.message.split('\n')[0]})`);
        await locator.evaluate(element => element.click());
    }
}
async function step(page, what, action) {
    try { return await action(); } catch (error) {
        await shot(`echec-${what}`);
        log(error.message.split('\n').slice(0, 12).join('\n'));
        await dumpPage(page);
        throw new Error(`${what} : ${error.message.split('\n')[0]}`);
    }
}
async function start() {
    await device.shell(`am start -W -n ${app}/.MainActivity`);
}

// Écran allumé et déverrouillé : sans cela la WebView ne dessine plus.
await device.shell('input keyevent KEYCODE_WAKEUP');
await device.shell('wm dismiss-keyguard');
await device.shell('svc power stayon true');

// 1. Premier lancement : l'application travaille seule ; l'écran de session mène au choix du serveur.
await device.shell(`am force-stop ${app}`);
await start();
let page = await appPage();
await step(page, 'serveur du cabinet', async () => {
    await press(page.locator('#shared-server-link'), { timeout: 120_000 });
    await page.fill('#shared-server-address', baseUrl);
    await shot('adresse-saisie');
    await press(page.locator('#shared-server-submit'));
    await page.locator('#shared-server-restart-now').waitFor();
});
await shot('serveur-retenu');
log(`serveur ${baseUrl} retenu`);
// L'application se ferme (Android : fermer puis rouvrir).
await page.locator('#shared-server-restart-now').evaluate(element => element.scrollIntoView({ block: 'center' })).catch(() => {});
await page.locator('#shared-server-restart-now').evaluate(element => element.click()).catch(() => {});
await sleep(3000);

// 2. Relancée, l'application est reliée au serveur : connexion du cabinet dans un onglet du navigateur.
await device.shell(`am force-stop ${app}`);
await start();
page = await appPage();
await step(page, 'bouton de connexion du cabinet', () => page.locator('a[data-sign-in]').waitFor({ timeout: 120_000 }));
await shot('session-serveur');
// La page de connexion s'ouvre dans un onglet de Chrome : Playwright s'y attache par le protocole de débogage (sans écran
// d'accueil de Chrome, options lues au démarrage dans /data/local/tmp/chrome-command-line, comme device.launchBrowser).
const devtools = 'lettra_devtools_remote';
await device.shell('am force-stop com.android.chrome');
await device.shell(`echo "_ --disable-fre --no-default-browser-check --no-first-run --remote-debugging-socket-name=${devtools}" > /data/local/tmp/chrome-command-line`);
// Sur une image non « debuggable », Chrome ne lit ce fichier que s'il est l'application de débogage.
await device.shell('am set-debug-app --persistent com.android.chrome');
execFileSync('adb', ['forward', 'tcp:9333', `localabstract:${devtools}`]);
await press(page.locator('a[data-sign-in]'));
log(`connexion demandée ; l'application affiche ${page.url()}`);
const keycloak = await keycloakPage(90_000).catch(async error => { await dumpPage(page); throw error; });
await shot('keycloak');
await keycloak.fill('#username', user);
await keycloak.fill('#password', password);
await keycloak.click('#kc-login');
await keycloak.waitForSelector('#otp', { timeout: 30_000 }).catch(async error => { await dumpPage(keycloak); throw error; });
await keycloak.fill('#otp', await freshCode());
await keycloak.click('#kc-login');
log('connexion du cabinet envoyée (mot de passe + code)');

// 3. De retour dans l'application : le client saisi dans le navigateur est là.
await step(page, 'accueil de l\'application connectée', () => page.locator('.lettra-user').waitFor({ timeout: 120_000 }));
await shot('connecte');
log(`connecté comme « ${await page.locator('.lettra-user').innerText()} »`);
await step(page, `client « ${browserCustomer} » du navigateur`, async () => {
    // Menu replié sur un téléphone : navigation interne de Blazor, comme un clic sur « Clients ».
    await page.evaluate(() => window.Blazor.navigateTo('customers'));
    await page.locator('#customers-list, #customers-empty').first().waitFor();
    await skipTour(page);
    await page.fill('#customers-search', browserCustomer);
    await page.locator('#customers-list [role=option]', { hasText: browserCustomer }).first().waitFor({ timeout: 30_000 });
});
await shot('client-du-navigateur');
log(`client du navigateur « ${browserCustomer} » vu sur l'appareil`);

// 4. Un client saisi sur l'appareil, que le navigateur retrouvera.
const name = `Client Android ${new Date().toISOString().replace(/\D/g, '').slice(0, 14)}`;
await step(page, `client « ${name} » enregistré`, async () => {
    await press(page.locator('#customers-new'));
    await page.locator('#name').waitFor({ timeout: 30_000 });
    await skipTour(page);
    await page.locator('label', { hasText: /Particulier|Individual/ }).locator('input[type=radio]').check().catch(async () => {
        await page.locator('input[type=radio]').nth(1).check();
    });
    await page.fill('#name', name);
    await page.fill('#email', 'android@client-demo.example');
    await page.fill('#line1', '2 rue de l\'Appareil');
    await page.fill('#zip', '75002');
    await page.fill('#city', 'Paris');
    await page.fill('#country', 'FR');
    await shot('client-saisi');
    await press(page.locator('form button[type=submit]'));
    // Confirmation par le toaster, puis la fiche du client.
    await page.locator('.lt-toast.is-success').first().waitFor({ timeout: 30_000 });
    await page.locator('#customer-sheet-title', { hasText: name }).waitFor({ timeout: 30_000 });
});
await shot('client-cree');
writeFileSync(join(out, 'client-appareil.txt'), name);
log(`client « ${name} » créé sur l'appareil`);
await device.close();
process.exit(0);
