// Régénère src/Presentation/Lettra.UI.Shared/wwwroot/data/codes-postaux.txt (aide à la saisie, vague 5, lane F).
// Usage : npm pack codes-postaux@<version> && tar xzf codes-postaux-<version>.tgz
//         node build/codes-postaux/generer.mjs package/codes-postaux.json
// Le fichier produit garde « code;commune », une ligne par couple, sans doublon, trié dans l'ordre des caractères.
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const source = process.argv[2];
if (!source) {
  console.error("Usage : node build/codes-postaux/generer.mjs <chemin de codes-postaux.json>");
  process.exit(1);
}

const rows = JSON.parse(fs.readFileSync(source, "utf8"));
const lines = [...new Set(rows
  .filter(r => /^\d{5}$/.test(r.codePostal) && r.nomCommune)
  .map(r => `${r.codePostal};${r.nomCommune.trim()}`))]
  .sort();

const target = path.join(path.dirname(fileURLToPath(import.meta.url)), "../../src/Presentation/Lettra.UI.Shared/wwwroot/data/codes-postaux.txt");
fs.writeFileSync(target, lines.join("\n") + "\n", "utf8");
console.log(`${lines.length} lignes écrites dans ${path.relative(process.cwd(), target)}`);
