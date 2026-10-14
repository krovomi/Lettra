#!/usr/bin/env node
/**
 * Régénère src/Lettra.Ai.Skills/skillpack.manifest.json : commit amont, licence, date de revue et SHA-256 de chaque
 * fichier embarqué. À lancer après une montée de version du skill pack, une fois la revue de l'expert faite et les
 * évals passées (annexe B §7). Usage : node tools/update-skillpack-manifest.js [--reviewed-on AAAA-MM-JJ --reviewed-by "Nom"]
 */
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const ROOT = path.join(__dirname, '..', 'src', 'Lettra.Ai.Skills');
const MANIFEST = path.join(ROOT, 'skillpack.manifest.json');
const args = process.argv.slice(2);
const arg = (name) => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : undefined; };
const previous = fs.existsSync(MANIFEST) ? JSON.parse(fs.readFileSync(MANIFEST, 'utf8')) : {};

function walk(dir) {
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap(e => {
    const full = path.join(dir, e.name);
    return e.isDirectory() ? walk(full) : [full];
  });
}

const files = {};
for (const sub of ['vendor', 'overlays']) {
  for (const file of walk(path.join(ROOT, sub)).sort()) {
    const rel = path.relative(ROOT, file).split(path.sep).join('/');
    files[rel] = crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
  }
}

const skillMd = fs.readFileSync(path.join(ROOT, 'vendor/paperasse/comptable/SKILL.md'), 'utf8');
const lastUpdated = (skillMd.match(/last_updated:\s*(\d{4}-\d{2}-\d{2})/) || [])[1];

const manifest = {
  source: 'https://github.com/romainsimon/paperasse',
  commit: 'f2e15c43112ddda9b80f53822185a21b6ad3b099',
  commitDate: '2026-08-10',
  license: 'MIT (c) 2026 Romain Simon ; données sous Licence Ouverte Etalab 2.0',
  skills: ['comptable', 'controleur-fiscal', 'commissaire-aux-comptes'],
  skillLastUpdated: lastUpdated,
  reviewedOn: arg('--reviewed-on') ?? previous.reviewedOn ?? null,
  reviewedBy: arg('--reviewed-by') ?? previous.reviewedBy ?? null,
  files,
};
fs.writeFileSync(MANIFEST, JSON.stringify(manifest, null, 2) + '\n');
console.log(`Manifest : ${Object.keys(files).length} fichiers, commit ${manifest.commit.slice(0, 7)}`);
