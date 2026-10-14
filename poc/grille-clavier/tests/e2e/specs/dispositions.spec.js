import { test, expect } from "@playwright/test";
import { COL, cell, ouvrir, active } from "./outils.js";

test("Ctrl+Shift+D bascule entre densité Expert (24 px) et Confort (40 px), sans perdre la cellule", async ({ page }) => {
  await ouvrir(page);
  const hauteur = () => page.locator('.lg-row[data-r="0"]').evaluate(e => e.getBoundingClientRect().height);
  expect(await hauteur()).toBe(24);
  await cell(page, 2, COL.pu).focus();
  await page.keyboard.press("Control+Shift+D");
  await expect.poll(hauteur).toBe(40);
  expect(await active(page)).toEqual({ r: 2, c: COL.pu });
  await page.keyboard.press("Control+Shift+D");
  await expect.poll(hauteur).toBe(24);
});

test.describe("écran de téléphone", () => {
  test.use({ viewport: { width: 390, height: 844 }, hasTouch: true });

  test("la même grille s'affiche en cartes, la navigation clavier reste identique", async ({ page }) => {
    await ouvrir(page);
    await expect(page.locator(".lg-header")).toBeHidden();
    const ligne = page.locator('.lg-row[data-r="0"]');
    expect(await ligne.evaluate(e => e.scrollWidth <= e.clientWidth)).toBe(true);   // pas de défilement horizontal
    await cell(page, 0, COL.tva).focus();
    await page.keyboard.press("Enter");
    expect(await active(page)).toEqual({ r: 1, c: COL.article });
    await page.keyboard.press("Control+End");
    await expect.poll(() => active(page)).toEqual({ r: 999, c: COL.article });
    await page.screenshot({ path: "results/telephone.png" });
  });
});
