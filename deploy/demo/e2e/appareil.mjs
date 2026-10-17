// Serveur commun, côté navigateur (docs/serveur-commun.md). Réutilise le secret TOTP enrôlé par parcours.mjs premier.
//   node appareil.mjs connexion <url>   page de connexion du cabinet ouverte par l'appareil (serveur-commun/), jusqu'au
//                                       retour par la boucle locale http://127.0.0.1:<port>/callback
//   node appareil.mjs voir <client>     le navigateur (BFF) retrouve le client saisi sur l'appareil
// Variables : LETTRA_URL (http://localhost:8080), LETTRA_DEMO_USER (demo), LETTRA_DEMO_PASSWORD, SORTIE (./sortie).
import { createHmac } from 'node:crypto';
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { chromium } from 'playwright';

const [mode, arg] = process.argv.slice(2);
const out = process.env.SORTIE ?? 'sortie';
const baseUrl = (process.env.LETTRA_URL ?? 'http://localhost:8080').replace(/\/$/, '');
const user = process.env.LETTRA_DEMO_USER ?? 'demo';
const password = process.env.LETTRA_DEMO_PASSWORD ?? 'demo-lettra-demo';
const secret = readFileSync(join(out, 'totp-secret.txt'), 'utf8').trim();
const stepFile = join(out, 'totp-periode.txt');
const log = m => console.log(`[navigateur ${new Date().toISOString()}] ${m}`);

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
    let last = existsSync(stepFile) ? Number(readFileSync(stepFile, 'utf8')) : -1;
    for (;;) {
        const step = Math.floor(Date.now() / 30_000); const left = 30 - (Math.floor(Date.now() / 1000) % 30);
        if (step > last && left > 5) { writeFileSync(stepFile, String(step)); return totp(step); }
        await new Promise(r => setTimeout(r, 1000));
    }
}
async function keycloak(page) {
    await page.locator('#username').waitFor({ timeout: 30_000 });
    await page.fill('#username', user);
    await page.fill('#password', password);
    await page.click('#kc-login');
    await page.locator('#otp').waitFor({ timeout: 30_000 });
    await page.fill('#otp', await freshCode());
    await page.click('#kc-login');
}

const browser = await chromium.launch();
try {
    const page = await browser.newPage();
    if (mode === 'connexion') {
        log(`page de connexion ouverte par l'appareil : ${new URL(arg).origin}${new URL(arg).pathname}`);
        await page.goto(arg);
        await keycloak(page);
        await page.waitForURL(u => u.href.startsWith('http://127.0.0.1:'), { timeout: 60_000 });
        await page.waitForLoadState('load').catch(() => {});
        await page.screenshot({ path: join(out, 'appareil-retour.png') });
        log(`retour à l'appareil : ${new URL(page.url()).origin}/callback`);
    } else if (mode === 'voir') {
        await page.goto(`${baseUrl}/`);
        await page.locator('a[data-sign-in]').click({ timeout: 60_000 });
        await keycloak(page);
        await page.locator('.lettra-user').waitFor({ timeout: 60_000 });
        await page.goto(`${baseUrl}/customers`);
        await page.locator('table.lettra-table td', { hasText: arg }).first().waitFor({ timeout: 30_000 });
        await page.screenshot({ path: join(out, 'navigateur-voit.png'), fullPage: true });
        log(`le navigateur voit « ${arg} »`);
    }
} finally {
    await browser.close();
}
