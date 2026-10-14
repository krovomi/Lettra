import { test, expect } from "@playwright/test";
import { COL, cell, ouvrir, active, attendreDotNet } from "./outils.js";

test.beforeEach(async ({ page }) => { await ouvrir(page); });

test("F4 ouvre la liste de choix, filtre, flèches et Entrée appliquent l'article", async ({ page }) => {
  await cell(page, 4, COL.article).focus();
  await page.keyboard.press("F4");
  const dialogue = page.getByRole("dialog");
  await expect(dialogue.locator("input")).toBeFocused();
  await page.keyboard.press("Control+A");
  await page.keyboard.type("bilan");
  await page.keyboard.press("ArrowDown");
  const choisi = await dialogue.locator('li[aria-selected="true"]').textContent();
  await page.keyboard.press("Enter");
  await expect(dialogue).toBeHidden();
  const [code, designation] = choisi.split(" · ");
  await expect(cell(page, 4, COL.article)).toHaveValue(code);
  await expect(cell(page, 4, COL.designation)).toHaveValue(designation);
  await expect.poll(() => active(page)).toEqual({ r: 4, c: COL.quantite });
});

test("Échap ferme la liste de choix et rend le focus à la cellule", async ({ page }) => {
  await cell(page, 4, COL.article).focus();
  await page.keyboard.press("F4");
  await expect(page.getByRole("dialog")).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(page.getByRole("dialog")).toBeHidden();
  await expect.poll(() => active(page)).toEqual({ r: 4, c: COL.article });
});

test("F3 recherche une ligne et y place le curseur, même hors écran", async ({ page }) => {
  await cell(page, 0, COL.article).focus();
  await page.keyboard.press("F3");
  await expect(page.getByRole("dialog").locator("input")).toBeFocused();
  await page.keyboard.type("850");                      // numéro de ligne
  await page.keyboard.press("Enter");
  await expect.poll(() => active(page)).toEqual({ r: 849, c: COL.article });
});

test("Insert insère une ligne vide sous la ligne active ; Ctrl+Suppr la supprime", async ({ page }) => {
  const suivante = await cell(page, 3, COL.article).inputValue();
  await cell(page, 2, COL.quantite).focus();
  await page.keyboard.press("Insert");
  await expect.poll(() => active(page)).toEqual({ r: 3, c: COL.article });
  await expect(cell(page, 3, COL.article)).toHaveValue("");
  await expect(cell(page, 4, COL.article)).toHaveValue(suivante);
  await page.keyboard.press("Control+Delete");
  await expect(cell(page, 3, COL.article)).toHaveValue(suivante);
  await expect.poll(() => active(page)).toEqual({ r: 3, c: COL.article });
});

test("F10 valide la ligne : elle passe en lecture seule et ne peut plus être supprimée", async ({ page }) => {
  await cell(page, 1, COL.designation).focus();
  await page.keyboard.press("F10");
  await expect(page.locator('.lg-row[data-r="1"]')).toHaveClass(/lg-validee/);
  await expect(cell(page, 1, COL.designation)).toHaveAttribute("readonly", "");
  await page.keyboard.press("Control+Delete");
  await expect(page.getByRole("status")).toContainText("ne peut plus être supprimée");
});

test("La barre de touches de fonction agit sur la cellule active (souris ou toucher)", async ({ page }) => {
  await cell(page, 6, COL.unite).focus();
  await page.getByRole("button", { name: /F4/ }).click();
  await expect(page.getByRole("dialog")).toBeVisible();
  await expect(page.getByRole("dialog").locator("h2")).toContainText("ligne 7");
});

test("F9 recalcule les totaux immédiatement", async ({ page }) => {
  await cell(page, 0, COL.quantite).focus();
  await page.keyboard.type("1000");
  await page.keyboard.press("F9");
  await attendreDotNet(page);
  await expect(page.getByRole("status")).toHaveText("Totaux recalculés.");
});
