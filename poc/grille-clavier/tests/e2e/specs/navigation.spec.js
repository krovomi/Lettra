import { test, expect } from "@playwright/test";
import { COL, cell, ouvrir, active, stats, texteCalcule, attendreDotNet } from "./outils.js";

test.beforeEach(async ({ page }) => { await ouvrir(page); });

test("Tab et Entrée parcourent les cellules saisissables puis passent à la ligne suivante, sans appel .NET", async ({ page }) => {
  await cell(page, 0, COL.article).focus();
  await page.evaluate(() => globalThis.__lettraGridStats.reset());
  const attendu = [COL.designation, COL.quantite, COL.unite, COL.pu, COL.remise, COL.tva];
  for (const c of attendu) {
    await page.keyboard.press("Tab");
    expect(await active(page)).toEqual({ r: 0, c });
  }
  await page.keyboard.press("Enter");                 // TVA → ligne suivante, Article (les montants calculés sont sautés)
  expect(await active(page)).toEqual({ r: 1, c: COL.article });
  await page.keyboard.press("Shift+Tab");
  expect(await active(page)).toEqual({ r: 0, c: COL.tva });
  await page.keyboard.press("Shift+Enter");
  expect(await active(page)).toEqual({ r: 0, c: COL.remise });
  expect((await stats(page)).dotnetCalls).toBe(0);
});

test("Flèches : haut/bas dans la colonne, gauche/droite entre colonnes, F2 passe en mode édition", async ({ page }) => {
  await cell(page, 5, COL.quantite).focus();
  await page.keyboard.press("ArrowDown");
  await page.keyboard.press("ArrowDown");
  expect(await active(page)).toEqual({ r: 7, c: COL.quantite });
  await page.keyboard.press("ArrowUp");
  expect(await active(page)).toEqual({ r: 6, c: COL.quantite });
  await page.keyboard.press("ArrowRight");
  expect(await active(page)).toEqual({ r: 6, c: COL.unite });
  await page.keyboard.press("ArrowLeft");
  expect(await active(page)).toEqual({ r: 6, c: COL.quantite });

  await page.keyboard.press("F2");
  await page.keyboard.press("ArrowLeft");             // en mode édition, le curseur bouge dans le texte
  expect(await active(page)).toEqual({ r: 6, c: COL.quantite });
  const curseur = await page.evaluate(() => document.activeElement.selectionStart);
  expect(curseur).toBe((await cell(page, 6, COL.quantite).inputValue()).length - 1);
});

test("Ctrl+Fin et Ctrl+Début traversent les 1 000 lignes virtualisées", async ({ page }) => {
  await cell(page, 0, COL.pu).focus();
  await page.keyboard.press("Control+End");
  await expect.poll(() => active(page)).toEqual({ r: 999, c: COL.pu });
  expect(await page.locator(".lg-row[data-r]").count()).toBeLessThan(120);   // seule la fenêtre visible est rendue
  await page.keyboard.press("Control+Home");
  await expect.poll(() => active(page)).toEqual({ r: 0, c: COL.pu });
  await page.keyboard.press("PageDown");
  const apres = await active(page);
  expect(apres.r).toBeGreaterThan(10);
});

test("Saisie validée à la sortie de la cellule : montants recalculés, format français", async ({ page }) => {
  await cell(page, 0, COL.quantite).focus();
  await page.keyboard.type("10");
  await page.keyboard.press("Tab");                   // Unité
  await page.keyboard.press("Tab");                   // PU HT
  await page.keyboard.type("1234,5");
  await page.keyboard.press("Tab");                   // Remise
  await page.keyboard.type("0");
  await page.keyboard.press("Tab");                   // TVA
  await page.keyboard.type("20");
  await page.keyboard.press("Tab");
  await attendreDotNet(page);
  await expect(cell(page, 0, COL.pu)).toHaveValue("1 234,50");
  expect((await texteCalcule(page, 0, COL.ht)).replace(/\s/g, " ")).toBe("12 345,00");
  expect((await texteCalcule(page, 0, COL.ttc)).replace(/\s/g, " ")).toBe("14 814,00");
  const s = await stats(page);
  // Qté, PU et remise ont changé : 3 appels .NET. La TVA ressaisie à l'identique (20) n'en fait aucun.
  expect(s.commit.count).toBe(3);
});

test("Valeur refusée : l'ancienne valeur revient et la cellule est signalée", async ({ page }) => {
  const avant = await cell(page, 2, COL.tva).inputValue();
  await cell(page, 2, COL.tva).focus();
  await page.keyboard.type("7");
  await page.keyboard.press("Enter");
  await expect(cell(page, 2, COL.tva)).toHaveValue(avant);
  await expect(cell(page, 2, COL.tva)).toHaveClass(/lg-error/);
});

test("Échap annule la saisie en cours de la cellule", async ({ page }) => {
  const avant = await cell(page, 3, COL.designation).inputValue();
  await cell(page, 3, COL.designation).focus();
  await page.keyboard.type("texte abandonné");
  await page.keyboard.press("Escape");
  await expect(cell(page, 3, COL.designation)).toHaveValue(avant);
  await page.keyboard.press("Tab");
  await attendreDotNet(page);
  expect((await stats(page)).commit.count).toBe(0);
});

test("Entrée sur la dernière cellule crée une ligne et place le curseur sur Article", async ({ page }) => {
  await cell(page, 0, COL.tva).focus();
  await page.keyboard.press("Control+End");
  await expect.poll(() => active(page)).toEqual({ r: 999, c: COL.tva });
  await page.keyboard.press("Enter");
  await expect.poll(() => active(page)).toEqual({ r: 1000, c: COL.article });
  await expect(page.locator(".entete small")).toHaveText(/1\s001 lignes/);
  await page.keyboard.type("PRE-001");
  await page.keyboard.press("Enter");
  await attendreDotNet(page);
  await expect(cell(page, 1000, COL.designation)).not.toHaveValue("");   // article connu : ligne complétée
});
