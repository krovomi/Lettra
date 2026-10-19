// Serveur commun sur Android (docs/serveur-commun.md) : l'application installée, dans un émulateur relié à la démo par
// « adb reverse », est reliée au serveur du cabinet par l'écran « Serveur du cabinet », se connecte (onglet du navigateur,
// mot de passe + code), voit le client saisi dans le navigateur et en crée un que le navigateur retrouvera
// (node appareil.mjs voir). L'interface est pilotée par l'accessibilité d'Android (Playwright _android).
//   node android.mjs <client du navigateur>
// Variables : LETTRA_URL (http://localhost:8080), LETTRA_DEMO_USER, LETTRA_DEMO_PASSWORD, SORTIE (./sortie).
// Écrit SORTIE/client-appareil.txt et des captures dans SORTIE/android/.
import { createHmac } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { _android as android } from 'playwright';

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
        const tree = await device.shell('uiautomator dump /dev/tty').then(b => b.toString()).catch(e => String(e));
        writeFileSync(join(shots, `echec-${what}.xml`), tree);
        // L'arbre résumé dans le journal de la CI (identifiants, textes, descriptions).
        for (const m of tree.matchAll(/<node [^>]*?text="([^"]*)"[^>]*?resource-id="([^"]*)"[^>]*?class="([^"]*)"[^>]*?content-desc="([^"]*)"/g))
            if (m[1] || m[2] || m[4]) console.log(`  ${m[3]} id=${m[2]} text=${m[1]} desc=${m[4]}`);
        throw new Error(`${what} : introuvable après ${timeout / 1000} s (capture et arbre dans ${shots}).`);
    }
}
// Saisie par l'accessibilité (sans clavier à l'écran, qui masquerait les boutons).
async function type(selector, text) {
    await device.fill(selector, text);
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
await device.tap({ text: /^(Sign in|Se connecter)$/ });
// Premier lancement de Chrome : écrans d'accueil à passer.
for (let i = 0; i < 6 && !(await visible(id('username'), 5_000)); i++) {
    for (const text of [/Accept & continue|Accepter et continuer/i, /Use without an account|Utiliser sans compte/i, /No thanks|Non merci/i, /^Continue$|^Continuer$/i, /^Got it$|^OK$/i]) {
        if (await visible({ text }, 500)) { await device.tap({ text }); await sleep(1000); }
    }
}
await waitFor(id('username'), 'page de connexion Keycloak', 60_000);
await shot('keycloak');
await type(id('username'), user);
await type(id('password'), password);
await device.tap(id('kc-login'));
await waitFor(id('otp'), 'code à usage unique');
await type(id('otp'), await freshCode());
await device.tap(id('kc-login'));
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
