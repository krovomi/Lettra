// Mise à jour de l'application dans un navigateur déjà ouvert, comme chez l'utilisateur après « git pull » puis
// « up --build » : un onglet reste ouvert pendant la mise à jour, un autre est ouvert juste après.
//
//   node mise-a-jour.mjs "<commande de mise à jour>"
//
// Vérifie, avec le même profil de navigateur (service worker et caches conservés) :
// - l'onglet ouvert depuis longtemps affiche « Nouvelle version de Lettra disponible — Recharger » sans recharger
//   tout seul (une saisie en cours n'est jamais perdue), puis passe à la nouvelle version par ce bouton ;
// - l'onglet ouvert juste après la mise à jour se recharge seul sur la nouvelle version ;
// - il ne reste qu'un cache de l'enveloppe, celui de la nouvelle version.
// La nouvelle version est reconnue à <meta name="lettra-build" content="$LETTRA_BUILD_ATTENDU"> dans index.html.
import { chromium } from 'playwright';
import { execSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const baseUrl = (process.env.LETTRA_URL ?? 'http://localhost:8080').replace(/\/$/, '');
const expected = process.env.LETTRA_BUILD_ATTENDU;
const out = process.env.SORTIE ?? 'sortie';
const command = process.argv[2];
if (!command || !expected) {
    console.error('usage : LETTRA_BUILD_ATTENDU=<valeur> node mise-a-jour.mjs "<commande de mise à jour>"');
    process.exit(2);
}
mkdirSync(out, { recursive: true });

const journal = [];
function log(line) {
    const stamped = `${new Date().toISOString()} ${line}`;
    journal.push(stamped);
    console.log(stamped);
}
function fail(message) {
    throw new Error(message);
}

const shellCaches = page => page.evaluate(async () => (await caches.keys()).filter(k => k.startsWith('lettra-shell-')));
const build = page => page.evaluate(() => document.querySelector('meta[name=lettra-build]')?.content ?? null);

// Lit la marque de version en tolérant un rechargement en cours (contexte d'exécution détruit).
async function waitForBuild(page, value, timeout) {
    const end = Date.now() + timeout;
    for (;;) {
        try {
            if (await build(page) === value) return;
        } catch { /* rechargement en cours */ }
        if (Date.now() > end) fail(`la page n'est pas passée à la version ${value} (marque lue : ${await build(page).catch(() => '?')})`);
        await new Promise(r => setTimeout(r, 500));
    }
}

const profile = mkdtempSync(join(tmpdir(), 'lettra-maj-'));
const context = await chromium.launchPersistentContext(profile, { locale: 'fr-FR', serviceWorkers: 'allow' });
await context.tracing.start({ screenshots: true, snapshots: true });
let failed = false;
try {
    // 1. Version actuelle installée et active, onglet resté ouvert.
    const ancien = await context.newPage();
    ancien.on('pageerror', e => log(`onglet ouvert : erreur de page : ${e.message}`));
    await ancien.goto(`${baseUrl}/`);
    await ancien.evaluate(() => navigator.serviceWorker.ready);
    if (!await ancien.evaluate(() => navigator.serviceWorker.controller !== null)) await ancien.reload();
    await ancien.waitForFunction(() => navigator.serviceWorker.controller !== null, null, { timeout: 120_000 });
    const avant = await shellCaches(ancien);
    log(`version installée : caches ${avant.join(', ')}, marque ${await build(ancien)}`);
    if (avant.length !== 1) fail(`un seul cache de l'enveloppe attendu avant la mise à jour : ${avant.join(', ')}`);
    if (await build(ancien) === expected) fail('la nouvelle version est déjà servie avant la mise à jour');
    let reloads = 0;
    ancien.on('framenavigated', frame => { if (frame === ancien.mainFrame()) reloads++; });
    // Au-delà de la fenêtre de rechargement automatique (10 s après l'ouverture).
    await ancien.waitForTimeout(11_000);

    // 2. Mise à jour du serveur pendant que l'onglet reste ouvert.
    log(`mise à jour : ${command}`);
    execSync(command, { stdio: 'inherit', shell: '/bin/bash' });
    log('serveur mis à jour');

    // 3. Nouvel onglet ouvert juste après : l'ancienne enveloppe s'affiche, puis la nouvelle version se recharge seule.
    const nouveau = await context.newPage();
    nouveau.on('pageerror', e => log(`nouvel onglet : erreur de page : ${e.message}`));
    await nouveau.goto(`${baseUrl}/`);
    await waitForBuild(nouveau, expected, 90_000);
    if (await nouveau.locator('#lettra-update').count()) fail('le nouvel onglet affiche le bandeau au lieu de se recharger');
    log('nouvel onglet : rechargé seul sur la nouvelle version');
    await nouveau.screenshot({ path: join(out, 'mise-a-jour-nouvel-onglet.png') });

    // 4. L'onglet resté ouvert propose de recharger, sans l'avoir fait tout seul.
    const bouton = ancien.locator('#lettra-update-reload');
    await bouton.waitFor({ timeout: 60_000 });
    const texte = await ancien.locator('#lettra-update').innerText();
    if (!/Nouvelle version de Lettra disponible/.test(texte) || !/Recharger/.test(texte)) fail(`bandeau inattendu : ${texte}`);
    if (reloads) fail('l\'onglet ouvert s\'est rechargé tout seul');
    log(`onglet ouvert : bandeau « ${texte.replace(/\s+/g, ' ').trim()} »`);
    // Bandeau visible et cliquable sur téléphone, tablette et ordinateur.
    for (const [nom, viewport] of [['telephone', { width: 375, height: 740 }], ['tablette', { width: 820, height: 1180 }], ['ordinateur', { width: 1440, height: 900 }]]) {
        await ancien.setViewportSize(viewport);
        const box = await bouton.boundingBox();
        if (!box || box.x < 0 || box.y < 0 || box.x + box.width > viewport.width || box.y + box.height > viewport.height) {
            fail(`bouton Recharger hors de l'écran (${nom}) : ${JSON.stringify(box)}`);
        }
        await ancien.screenshot({ path: join(out, `mise-a-jour-bandeau-${nom}.png`) });
    }
    await bouton.click();
    await waitForBuild(ancien, expected, 60_000);
    log('onglet ouvert : nouvelle version après « Recharger »');

    // 5. Seul le cache de la nouvelle version reste.
    const apres = await shellCaches(ancien);
    log(`caches après la mise à jour : ${apres.join(', ')}`);
    if (apres.length !== 1 || apres[0] === avant[0]) fail(`un seul cache, celui de la nouvelle version, attendu : ${apres.join(', ')}`);
    log('OK : un navigateur déjà ouvert passe à la nouvelle version');
} catch (e) {
    failed = true;
    log(`ÉCHEC : ${e.message}`);
} finally {
    await context.tracing.stop({ path: join(out, 'mise-a-jour-trace.zip') }).catch(() => {});
    await context.close();
    writeFileSync(join(out, 'mise-a-jour.log'), journal.join('\n') + '\n');
}
process.exit(failed ? 1 : 0);
