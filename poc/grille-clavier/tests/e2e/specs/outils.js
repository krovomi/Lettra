// Outils partagés des tests de la grille.

export const COL = { article: 0, designation: 1, quantite: 2, unite: 3, pu: 4, remise: 5, tva: 6, ht: 7, ttc: 8 };

export const cell = (page, r, c) => page.locator(`input.lg-input[data-r="${r}"][data-c="${c}"]`);

export async function ouvrir(page) {
  await page.goto("/");
  await cell(page, 0, 0).waitFor({ timeout: 60_000 });
  await page.evaluate(() => globalThis.__lettraGridStats?.reset());
}

export async function active(page) {
  return page.evaluate(() => {
    const el = document.activeElement;
    return el?.classList.contains("lg-input") ? { r: Number(el.dataset.r), c: Number(el.dataset.c) } : null;
  });
}

export const stats = page => page.evaluate(() => ({ ...globalThis.__lettraGridStats.summary(), dotnetCalls: globalThis.__lettraGridStats.dotnetCalls }));

export const texteCalcule = (page, r, c) =>
  page.locator(`.lg-row[data-r="${r}"] .lg-cell:nth-child(${c + 1}) .lg-calc`).textContent();

// Attend que les appels .NET en cours soient terminés (validation de cellule, commande).
export async function attendreDotNet(page) {
  await page.waitForTimeout(50);
  await page.evaluate(() => new Promise(r => requestAnimationFrame(() => setTimeout(r, 0))));
}
