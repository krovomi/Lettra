import { test, expect } from "@playwright/test";
import fs from "node:fs";
import { COL, cell, ouvrir, stats, attendreDotNet } from "./outils.js";

// Critère de l'annexe C : 1 000 lignes, Entrée/Tab/F-keys sans latence perceptible.
//
// Latence mesurée dans la page : de l'évènement keydown à la fin de l'image qui affiche le résultat.
// Cadence : une frappe toutes les 40 ms (25 frappes/s, l'ordre de la répétition automatique d'une touche
// maintenue). Sans cadence, Playwright envoie ~300 frappes/s et mesure surtout la file d'attente.
// Le « poste standard du cabinet » est approché en ralentissant le processeur de Chromium (×4).
// Seuil : 100 ms est le seuil usuel de perception d'un retard ; on vise la moitié sur un poste normal.
// « commit » mesure la mise à jour des montants après une saisie : elle est asynchrone, le curseur est déjà
// sur la cellule suivante, donc elle ne retarde pas la frappe. Sur poste lent on accepte 0,3 s (constat du POC).
const CADENCE_MS = Number(process.env.LETTRA_CADENCE_MS ?? 40);
const SEUILS = {
  1: { nav: 50, command: 100, commit: 100 },
  4: { nav: 100, command: 150, commit: 300 },
};
const RALENTISSEMENTS = (process.env.LETTRA_CPU_THROTTLE ?? "1,4").split(",").map(Number);

async function frappe(page, touche, fois = 1) {
  for (let i = 0; i < fois; i++) {
    await page.keyboard.press(touche);
    await page.waitForTimeout(CADENCE_MS);
  }
}

for (const ralentissement of RALENTISSEMENTS) {
  test(`1 000 lignes : 1 000 frappes de navigation et 100 commandes, processeur ×${ralentissement}`, async ({ page }) => {
    test.setTimeout(300_000);
    await ouvrir(page);
    const cdp = await page.context().newCDPSession(page);
    await cdp.send("Emulation.setCPUThrottlingRate", { rate: ralentissement });

    await cell(page, 0, COL.article).focus();
    await page.evaluate(() => globalThis.__lettraGridStats.reset());

    // 1) Navigation pure : Tab sur 60 lignes, puis Entrée et flèche bas (lignes virtualisées rendues en défilant).
    await frappe(page, "Tab", 7 * 60);
    await frappe(page, "Enter", 300);
    await frappe(page, "ArrowDown", 280);
    await attendreDotNet(page);
    const navigation = await stats(page);
    expect(navigation.nav.count).toBe(1000);
    expect(navigation.dotnetCalls).toBe(0);              // aucun appel .NET pendant la navigation

    // 2) Commandes .NET : F9 (recalcul), puis 50 saisies de quantité validées en sortie de cellule.
    await frappe(page, "ArrowLeft", 4);                  // colonne TVA → Qté
    await frappe(page, "F9", 50);
    for (let i = 0; i < 50; i++) {
      await page.keyboard.type(String(i + 1));
      await frappe(page, "ArrowDown");
    }
    await attendreDotNet(page);
    await page.waitForTimeout(500);
    const total = await stats(page);
    await cdp.send("Emulation.setCPUThrottlingRate", { rate: 1 });

    fs.mkdirSync("results", { recursive: true });
    fs.writeFileSync(`results/latences-cpu-x${ralentissement}.json`, JSON.stringify({ cadenceMs: CADENCE_MS, ...total }, null, 2));
    console.log(`processeur ×${ralentissement}`, JSON.stringify(total));

    const seuil = SEUILS[ralentissement] ?? SEUILS[4];
    expect(total.nav.p95).toBeLessThan(seuil.nav);
    expect(total.command.p95).toBeLessThan(seuil.command);
    expect(total.commit.p95).toBeLessThan(seuil.commit);
  });
}

test("Rechargement de 10 000 lignes et saut à la dernière", async ({ page }) => {
  await ouvrir(page);
  await page.getByRole("button", { name: "10 000 lignes" }).click();
  await expect(page.locator(".entete small")).toHaveText(/10\s000 lignes/);
  const ms = Number(await page.locator(".mesure").getAttribute("data-render-ms"));
  console.log("rendu 10 000 lignes (ms)", ms);
  expect(ms).toBeLessThan(1000);
  await cell(page, 0, COL.article).focus();
  await page.keyboard.press("Control+End");
  await expect.poll(() => page.evaluate(() => document.activeElement.dataset.r)).toBe("9999");
});
