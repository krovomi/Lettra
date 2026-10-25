// Démo commentée jouée de bout en bout dans Chromium : connexion (mot de passe + TOTP déjà enrôlé par parcours.mjs),
// écran /demo, lecture complète en vitesse « très rapide » avec « Suivant » pendant chaque bulle, jusqu'à « Démo
// terminée ». Échoue si une étape se bloque (élément attendu absent). Données fictives uniquement.
//
//   node demo-commentee.mjs [largeur] [langue]     ex. node demo-commentee.mjs 390 en-US
//
// Variables : LETTRA_URL (http://localhost:8080), LETTRA_DEMO_USER (demo), LETTRA_DEMO_PASSWORD, SORTIE (./sortie,
// doit contenir totp-secret.txt écrit par « node parcours.mjs premier »).
import { createHmac } from 'node:crypto';
import { mkdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { chromium } from 'playwright';

const width = Number(process.argv[2] ?? 1440);
const locale = process.argv[3] ?? 'fr-FR';
const baseUrl = (process.env.LETTRA_URL ?? 'http://localhost:8080').replace(/\/$/, '');
const out = process.env.SORTIE ?? 'sortie';
const password = process.env.LETTRA_DEMO_PASSWORD ?? (() => { throw new Error('LETTRA_DEMO_PASSWORD manquant'); })();
mkdirSync(out, { recursive: true });
const secret = readFileSync(join(out, 'totp-secret.txt'), 'utf8').trim();
const log = m => console.log(`[${new Date().toISOString()}] ${m}`);

function totp(text) {
    const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
    let bits = '';
    for (const c of text.replace(/[\s=]/g, '').toUpperCase()) bits += alphabet.indexOf(c).toString(2).padStart(5, '0');
    const key = Buffer.from(bits.match(/.{8}/g).map(b => parseInt(b, 2)));
    const counter = Buffer.alloc(8);
    counter.writeBigUInt64BE(BigInt(Math.floor(Date.now() / 30_000)));
    const h = createHmac('sha256', key).update(counter).digest();
    const o = h[h.length - 1] & 0xf;
    return String((h.readUInt32BE(o) & 0x7fffffff) % 1_000_000).padStart(6, '0');
}

const browser = await chromium.launch();
const page = await browser.newPage({ locale, viewport: { width, height: width < 768 ? 844 : 900 } });
page.on('pageerror', e => log(`erreur de page : ${e.message}`));
try {
    await page.goto(`${baseUrl}/`);
    await page.locator('a[data-sign-in]').click();
    await page.fill('#username', process.env.LETTRA_DEMO_USER ?? 'demo');
    await page.fill('#password', password);
    await page.click('#kc-login');
    // Un code déjà utilisé dans la même période est refusé : attendre la période suivante si la fin est proche.
    while (30 - (Math.floor(Date.now() / 1000) % 30) < 5) await page.waitForTimeout(1000);
    await page.fill('#otp', totp(secret));
    await page.click('#kc-login');
    await page.locator('.lettra-user').waitFor({ state: 'attached', timeout: 60_000 });

    await page.goto(`${baseUrl}/demo`);
    await page.locator('#demo-start').waitFor({ timeout: 60_000 });
    await page.selectOption('#demo-page-speed', '2');
    await page.click('#demo-start');
    const started = Date.now();
    let last = '';
    for (;;) {
        if (Date.now() - started > 10 * 60_000) throw new Error('démo commentée trop longue');
        const s = await page.evaluate(() => ({
            step: `${document.querySelector('#demo-count')?.textContent} | ${document.querySelector('#demo-step-title')?.textContent}`,
            acting: !!document.querySelector('#demo-acting'),
            blocked: !!document.querySelector('#demo-blocked'),
            done: !!document.querySelector('#demo-replay'),
        }));
        if (s.step !== last) { log(s.step); last = s.step; }
        if (s.blocked) {
            await page.screenshot({ path: join(out, `demo-commentee-${width}-bloquee.png`), fullPage: true });
            throw new Error(`étape bloquée : ${s.step}`);
        }
        if (s.done) break;
        if (!s.acting) await page.click('#demo-next', { timeout: 5_000 }).catch(() => {});
        await page.waitForTimeout(400);
    }
    await page.screenshot({ path: join(out, `demo-commentee-${width}-${locale}.png`) });
    log(`démo commentée terminée en ${Math.round((Date.now() - started) / 1000)} s (${width} px, ${locale})`);
} finally {
    await browser.close();
}
