#!/usr/bin/env node
// Captures du système de design (docs/design/systeme-de-design.md) : photographie chaque page de la galerie produite par
// GalleryTests à 375, 768 et 1440 px, et passe l'audit axe (WCAG 2.2 AA) quand axe-core est disponible.
//
//   LETTRA_GALLERY_DIR=$PWD/artifacts/galerie dotnet test tests/Lettra.UI.Tests --filter Galerie_des_ecrans
//   NODE_PATH=$(npm root -g) node build/design-captures.cjs artifacts/galerie [chemin/vers/axe.min.js]
//
// Playwright est celui de la machine (npx playwright ou paquet global) ; aucune dépendance n'est ajoutée au dépôt.
'use strict';
const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const gallery = path.resolve(process.argv[2] ?? 'artifacts/galerie');
const axePath = process.argv[3];
const wwwroot = path.resolve(__dirname, '../src/Presentation/Lettra.UI.Shared/wwwroot');
const widths = [375, 768, 1440];

// Feuilles de style et polices à côté des pages, comme dans l'application.
fs.mkdirSync(path.join(gallery, 'css', 'fonts'), { recursive: true });
for (const file of fs.readdirSync(wwwroot).filter(f => f.endsWith('.css'))) {
  fs.copyFileSync(path.join(wwwroot, file), path.join(gallery, 'css', file));
}
for (const file of fs.readdirSync(path.join(wwwroot, 'fonts'))) {
  fs.copyFileSync(path.join(wwwroot, 'fonts', file), path.join(gallery, 'css', 'fonts', file));
}

(async () => {
  const browser = await chromium.launch(process.env.PLAYWRIGHT_CHROMIUM ? { executablePath: process.env.PLAYWRIGHT_CHROMIUM } : {});
  const shots = path.join(gallery, 'captures');
  fs.mkdirSync(shots, { recursive: true });
  const report = [];
  const pages = fs.readdirSync(gallery).filter(f => f.endsWith('.html')).sort();
  for (const file of pages) {
    for (const width of widths) {
      // Mouvements réduits : les panneaux s'affichent sans animation, la capture les montre entiers.
      const page = await browser.newPage({ viewport: { width, height: width < 768 ? 812 : 900 }, deviceScaleFactor: 1, reducedMotion: 'reduce' });
      await page.goto('file://' + path.join(gallery, file));
      await page.evaluate(() => document.fonts.ready);
      const name = file.replace(/\.html$/, `.${width}.png`);
      await page.screenshot({ path: path.join(shots, name), fullPage: true });
      // Aucun défilement horizontal de la page, quelle que soit la largeur.
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
      if (overflow > 0) report.push({ page: name, rule: 'horizontal-overflow', nodes: [`${overflow}px`] });
      if (axePath) {
        await page.addScriptTag({ path: axePath });
        const result = await page.evaluate(async () => await axe.run(document, {
          runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'] },
        }));
        for (const v of result.violations) {
          report.push({ page: name, rule: v.id, impact: v.impact, nodes: v.nodes.slice(0, 5).map(n => n.target.join(' ') + ' : ' + n.failureSummary.split('\n').slice(1).join(' ')) });
        }
      }
      await page.close();
    }
  }
  await browser.close();
  fs.writeFileSync(path.join(gallery, 'audit.json'), JSON.stringify(report, null, 2));
  console.log(`${pages.length * widths.length} captures dans ${shots}, ${report.length} constat(s) dans audit.json`);
  process.exitCode = report.length === 0 ? 0 : 1;
})();
