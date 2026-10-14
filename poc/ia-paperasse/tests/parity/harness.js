#!/usr/bin/env node
/**
 * Banc de parité Lettra ↔ Paperasse.
 *
 * Exécute les scripts de référence Paperasse (copiés tels quels depuis le commit f2e15c4 dans ./paperasse/scripts)
 * sur un jeu de cas généré de façon déterministe, et écrit les résultats attendus dans ./golden/*.json.
 * Les tests C# (Lettra.Compliance.Tests) rejouent ces cas sur le port C# et exigent le même résultat.
 *
 * Usage : node harness.js [dossier-de-sortie]   (défaut : ./golden)
 *
 * Les scripts sont chargés dans un contexte vm isolé : leur main() n'est pas lancé, la date du jour
 * est fixée par cas, console.log et process.exit sont capturés. Aucun script n'est modifié sur disque.
 */
const fs = require('fs');
const path = require('path');
const vm = require('vm');

const SCRIPTS = path.join(__dirname, 'paperasse', 'scripts');
const MENTIONS = path.join(__dirname, '..', '..', 'src', 'Lettra.Ai.Skills', 'vendor', 'paperasse',
  'comptable', 'data', 'facturation', 'mentions-obligatoires.json');
const OUT = process.argv[2] || path.join(__dirname, 'golden');

// ---------------------------------------------------------------------------
// Chargement des scripts de référence dans un bac à sable
// ---------------------------------------------------------------------------

class ExitSignal extends Error { constructor(code) { super('exit ' + code); this.code = code; } }

function loadScript(file, stripMain) {
  let source = fs.readFileSync(path.join(SCRIPTS, file), 'utf8');
  if (source.startsWith('#!')) source = '//' + source;
  source = source.replace(stripMain, '/* main() retiré par le banc de parité */');
  const state = { now: '2026-10-14T12:00:00Z', out: [], err: [] };
  const RealDate = Date;
  class FixedDate extends RealDate {
    constructor(...args) { if (args.length === 0) super(state.now); else super(...args); }
    static now() { return new RealDate(state.now).getTime(); }
  }
  const sandbox = {
    Date: FixedDate,
    console: { log: (...a) => state.out.push(a.join(' ')), error: (...a) => state.err.push(a.join(' ')) },
    process: { argv: ['node', file], exit: (code) => { throw new ExitSignal(code); }, cwd: () => __dirname, env: {} },
    require: (m) => require(m),
    module: { exports: {} },
    __dirname: SCRIPTS,
    __filename: path.join(SCRIPTS, file),
    BigInt, Number, String, Math, JSON, Object, Array, RegExp, Error,
  };
  sandbox.exports = sandbox.module.exports;
  vm.createContext(sandbox);
  vm.runInContext(source, sandbox, { filename: file });
  return { sandbox, state };
}

const validate = loadScript('validate-facture.js', /\nmain\(\);\s*$/);
const calc = loadScript('calc.js', /\nif \(require\.main === module\) \{\s*main\(\);\s*\}/);
const facturx = loadScript('generate-facturx.js', /\nmain\(\)\.catch\([\s\S]*$/);
const mentions = JSON.parse(fs.readFileSync(MENTIONS, 'utf8'));

for (const [name, fn] of [['validateInvoice', validate], ['main', calc], ['computeTotals', facturx]]) {
  if (typeof fn.sandbox[name] !== 'function') throw new Error(`fonction ${name} introuvable dans le script de référence`);
}

function runValidate(c) {
  validate.state.now = c.today + 'T12:00:00Z';
  return validate.sandbox.validateInvoice(c.company, c.invoice, mentions, c.strict);
}

function runCalc(c) {
  calc.state.out = []; calc.state.err = [];
  calc.sandbox.process.argv = ['node', 'calc.js', c.command, ...Object.entries(c.args).map(([k, v]) => `--${k}=${v}`)];
  let exitCode = 0;
  try { calc.sandbox.main(); } catch (e) { if (e instanceof ExitSignal) exitCode = e.code; else throw e; }
  return { exitCode, stdout: calc.state.out.join('\n'), stderr: calc.state.err.join('\n') };
}

function runTotals(c) {
  const t = facturx.sandbox.computeTotals(c.company, c.invoice);
  return {
    totalHT: t.totalHT.toFixed(2), totalTVA: t.totalTVA.toFixed(2), totalTTC: t.totalTTC.toFixed(2),
    tvaRate: t.tvaRate, isFranchise: !!t.isFranchise,
  };
}

// ---------------------------------------------------------------------------
// Générateur pseudo-aléatoire déterministe (mulberry32)
// ---------------------------------------------------------------------------

function rng(seed) {
  return () => {
    seed |= 0; seed = seed + 0x6D2B79F5 | 0;
    let t = Math.imul(seed ^ seed >>> 15, 1 | seed);
    t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t;
    return ((t ^ t >>> 14) >>> 0) / 4294967296;
  };
}
const R = rng(20261014);
const pick = (xs) => xs[Math.floor(R() * xs.length)];
const maybe = (p = 0.5) => R() < p;
function put(obj, key, values, pAbsent = 0.25) { if (!maybe(pAbsent)) obj[key] = pick(values); }

// ---------------------------------------------------------------------------
// Cas : mentions obligatoires
// ---------------------------------------------------------------------------

// Base complète et conforme, puis 0 à 4 altérations : on obtient un mélange réaliste de factures
// conformes, avec avertissements et non conformes, qui balaie chaque branche du script.
function baseCompany() {
  return {
    name: 'WebCraft', legal_form: 'SASU', address: '12 rue du Commerce, 69002 Lyon',
    siren: '912345678', siret: '91234567800014', rcs: 'RCS Lyon', tva_intracom: 'FR40912345678',
    tax: { regime_tva: 'reel_normal' },
    payment: { late_penalty_rate: '3x_legal', escompte_label: "Pas d'escompte pour paiement anticipe" },
  };
}
function baseInvoice() {
  return {
    number: 'F-2026-001', date: '2026-09-15', due_date: '2026-10-15', category: 'services',
    client: { name: 'TechSolutions SAS', address: '45 avenue de la République, 75011 Paris', country: 'FR', siren: '987654321' },
    lines: [{ description: 'Développement application web', quantity: 10, unit_price: 500 }],
    payment: { terms: '30 jours date de facture' },
  };
}
const companyMutations = [
  c => { c.name = pick(['', 'Atelier Éole SARL']); },
  c => { delete c.address; },
  c => { c.siren = ''; },
  c => { delete c.siren; delete c.siret; },
  c => { c.rcs = ''; },
  c => { c.legal_form = pick(['EI', 'Micro-entreprise', '', 'SARL', 'SCI']); },
  c => { delete c.tva_intracom; },
  c => { c.tax.regime_tva = 'franchise'; delete c.tva_intracom; },
  c => { delete c.tax; },
  c => { c.tax.option_debits = pick([true, false]); },
  c => { delete c.payment.late_penalty_rate; },
  c => { c.payment.escompte_label = ''; },
  c => { delete c.payment; },
];
const invoiceMutations = [
  i => { i.number = ''; },
  i => { delete i.date; },
  i => { delete i.due_date; },
  i => { delete i.due_date; i.payment.terms = ''; },
  i => { delete i.category; },
  i => { i.category = pick(['biens', 'mixte', '']); },
  i => { i.delivery_date = pick(['2026-09-10', '']); },
  i => { i.delivery_address = pick(['Entrepôt 3, 93200 Saint-Denis', '', i.client ? i.client.address : 'x']); },
  i => { delete i.client; },
  i => { i.client.name = pick(['Jean Martin', 'dupont sarl', 'ACME GmbH', 'Société Générale', 'Les Sasquatch',
    'Nordwind AG', 'Café Été SA', 'ÉSAS Conseil', 'Smith & Co Ltd', '']); },
  i => { delete i.client.siren; },
  i => { i.client.country = pick(['fr', 'DE', 'de', 'BE', 'US', 'CH', '']); },
  i => { i.client.tva_intracom = pick(['DE123456789', '']); },
  i => { i.client.address = ''; },
  i => { i.lines = []; },
  i => { delete i.lines; },
  i => { i.lines.push({ description: pick(['Conseil', '']), quantity: pick([1, 0, 2.5]), unit_price: pick([99, 0, 120.5]) }); },
  i => { delete i.lines[0].quantity; },
  i => { delete i.lines[0].unit_price; },
  i => { i.lines[0].description = ''; },
  i => { delete i.payment; },
  i => { i.payment.late_penalty_rate = '3x_legal'; },
  i => { i.payment.escompte_label = pick(["Pas d'escompte", '']); },
];
function mutate(obj, mutations, maxCount) {
  const n = Math.floor(R() * (maxCount + 1));
  for (let k = 0; k < n; k++) {
    try { pick(mutations)(obj); } catch { /* altération sans objet (champ déjà retiré) */ }
  }
  return obj;
}
function genCompany() { return mutate(baseCompany(), companyMutations, 2); }
function genInvoice() { return mutate(baseInvoice(), invoiceMutations, 3); }

function readEvalFile(name) {
  return JSON.parse(fs.readFileSync(path.join(__dirname, 'paperasse', 'evals', 'files', name), 'utf8'));
}

const validateCases = [];
// Cas fixes : fichiers d'évaluation Paperasse et gabarit
const webdev = readEvalFile('company-webdev.json');
const invoiceTest = readEvalFile('invoice-test.json');
for (const today of ['2026-08-31', '2026-09-01', '2026-10-14']) {
  for (const strict of [false, true]) {
    validateCases.push({ name: `eval-invoice-test-${today}-${strict}`, company: webdev, invoice: invoiceTest, today, strict });
  }
}
for (let i = 0; i < 1000; i++) {
  validateCases.push({
    name: `fuzz-${i}`, company: genCompany(), invoice: genInvoice(),
    today: pick(['2026-08-31', '2026-09-01', '2026-10-14', '2027-09-01']), strict: maybe(0.3),
  });
}
for (const c of validateCases) c.expected = JSON.parse(JSON.stringify(runValidate(c)));

// ---------------------------------------------------------------------------
// Cas : calc.js
// ---------------------------------------------------------------------------

const amounts = ['1200', '850', '2499.99', '1200', '1 200,50', '-35.07', '0', '12.345', 'abc', '3000', '50000', '-1000', '12000', '1000', '42500,5', '99999999999.99', '0,1'];
const ints = ['92', '30', '120', '92', '365', '0', '-1', '3.5', '200', '182', '366', '50', '1', '7'];
const rates = ['25', '15', '1', '15,5', '0.333', '33.335', '1e2', '-5', '0', '12.5'];
const calcCases = [
  // Cas du test officiel test-deterministic-calculations.js
  { command: 'cca', args: { total: '1200', 'jours-n-plus-1': '92', 'jours-totaux': '365' } },
  { command: 'amortissement-lineaire', args: { valeur: '3000', duree: '3', 'jours-utilises': '200', 'base-jours': '365' } },
  { command: 'is', args: { 'resultat-fiscal': '10000', taux: '25' } },
  { command: 'is', args: { 'resultat-fiscal': '1000', taux: '1' } },
  { command: 'is', args: { 'resultat-fiscal': '-1000', taux: '25' } },
  { command: 'is', args: { 'resultat-fiscal': '50000', 'taux-reduit': '15', 'taux-normal': '25', 'jours-exercice': '182' } },
  { command: 'tva-acomptes-rs', args: { 'tva-n-1': '12000' } },
  { command: 'prorata', args: { montant: '1000', jours: '50', base: '365' } },
  { command: 'is', args: { 'resultat-fiscal': '50000' } },
  { command: 'inconnue', args: {} },
];
function maybeArg(args, key, values, pAbsent = 0.15) { if (!maybe(pAbsent)) args[key] = pick(values); }
for (let i = 0; i < 1000; i++) {
  const command = pick(['cca', 'amortissement-lineaire', 'is', 'tva-acomptes-rs', 'prorata']);
  const args = {};
  switch (command) {
    case 'cca': maybeArg(args, 'total', amounts); maybeArg(args, 'jours-n-plus-1', ints); maybeArg(args, 'jours-totaux', ints); break;
    case 'prorata': maybeArg(args, 'montant', amounts); maybeArg(args, 'jours', ints); maybeArg(args, 'base', ints, 0.5); break;
    case 'amortissement-lineaire':
      maybeArg(args, 'valeur', amounts); maybeArg(args, 'duree', ints);
      maybeArg(args, 'jours-utilises', ints, 0.5); maybeArg(args, 'base-jours', ints, 0.5); break;
    case 'is':
      maybeArg(args, 'resultat-fiscal', amounts);
      if (maybe()) maybeArg(args, 'taux', rates, 0);
      else {
        maybeArg(args, 'taux-reduit', rates, 0.4); maybeArg(args, 'taux-normal', rates, 0.4);
        maybeArg(args, 'plafond', amounts, 0.5); maybeArg(args, 'jours-exercice', ints, 0.5);
      }
      break;
    case 'tva-acomptes-rs': maybeArg(args, 'tva-n-1', amounts); break;
  }
  calcCases.push({ command, args });
}
calcCases.forEach((c, i) => { c.name = `calc-${i}`; c.expected = runCalc(c); });

// ---------------------------------------------------------------------------
// Cas : totaux de facture (computeTotals de generate-facturx.js)
// ---------------------------------------------------------------------------

const totalsCases = [];
for (let i = 0; i < 1000; i++) {
  const company = { tax: {} };
  if (maybe(0.2)) company.tax.regime_tva = 'franchise';
  const rate = pick([0.20, 0.10, 0.055, 0.021, 0, undefined]);
  if (rate !== undefined) company.tax.tva_rate = rate;
  const lines = [];
  const n = 1 + Math.floor(R() * 5);
  for (let j = 0; j < n; j++) {
    lines.push({
      description: 'Ligne ' + j,
      quantity: pick([1, 2, 3, 0.5, 1.5, 0.25, 7, 12, 0.333, 1.125, 2.75]),
      unit_price: Math.round(R() * 200000) / 100,
    });
  }
  totalsCases.push({ name: `totals-${i}`, company, invoice: { lines } });
}
for (const c of totalsCases) c.expected = runTotals(c);

// ---------------------------------------------------------------------------

fs.mkdirSync(OUT, { recursive: true });
const write = (file, cases) => fs.writeFileSync(path.join(OUT, file), JSON.stringify({
  reference: 'romainsimon/paperasse@f2e15c4',
  generator: 'tests/parity/harness.js',
  cases,
}) + "\n");
write('validate-facture.json', validateCases);
write('calc.json', calcCases);
write('totals.json', totalsCases);
console.log(`Parité : ${validateCases.length} factures, ${calcCases.length} calculs, ${totalsCases.length} totaux → ${OUT}`);
