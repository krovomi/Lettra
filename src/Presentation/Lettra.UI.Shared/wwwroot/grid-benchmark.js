// Mesure de la grille sur le poste (critère de sortie J1, annexe C) : rejoue au clavier une saisie sur une pièce
// de 1 000 lignes, à cadence humaine, et relève la latence de chaque frappe, de l'envoi de la touche à la fin de
// l'image qui affiche son résultat. Les touches passent par le vrai module clavier (grid-keyboard.js).

const frame = () => new Promise(resolve => requestAnimationFrame(() => setTimeout(resolve, 0)));
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

// Attend qu'une condition soit vraie, image par image ; renvoie false si le délai est dépassé.
async function until(condition, timeoutMs) {
  const end = performance.now() + timeoutMs;
  while (!condition()) {
    if (performance.now() > end) return false;
    await frame();
  }
  return true;
}

function press(target, key) {
  target.dispatchEvent(new KeyboardEvent("keydown", { key, bubbles: true, cancelable: true }));
}

// Une frappe de navigation : la latence court jusqu'à ce que le curseur ait quitté la cellule et que l'image suive.
async function navigate(key) {
  const before = document.activeElement;
  const t0 = performance.now();
  press(before, key);
  const moved = await until(() => document.activeElement !== before, 2000);
  await frame();
  return moved ? performance.now() - t0 : null;
}

// Une saisie : le nombre est tapé dans la cellule, puis Tab. Deux mesures : la frappe Tab (le curseur suit-il ?)
// et la mise à jour du montant de la ligne, qui passe par .NET (validation de la cellule puis rendu de la ligne).
async function typeAndCommit(value) {
  const cell = document.activeElement;
  const row = cell.closest(".lg-row");
  const amount = () => row.querySelector(".lg-calc")?.textContent ?? "";
  const previous = amount();
  // Une quantité identique ne changerait pas le montant : on tape alors la suivante.
  cell.value = cell.value.trim() === value ? String(Number(value) + 1) : value;
  cell.dispatchEvent(new Event("input", { bubbles: true }));
  const t0 = performance.now();
  const tab = await navigate("Tab");
  const updated = await until(() => amount() !== previous, 3000);
  await frame();
  return { tab, amount: updated ? performance.now() - t0 : null };
}

export async function run(host, options) {
  const root = host.querySelector(".lg-grid");
  const first = root?.querySelector('input.lg-input[data-r="0"]');
  if (!first) throw new Error("grille absente");
  first.focus();
  await frame();

  const cadence = options.cadenceMs;
  const editableCount = options.editableColumns;
  const result = { navigation: [], entry: [], amount: [], missed: 0, rows: 0 };
  let sent = 0;
  let cycle = 0;

  const record = (list, value) => (value === null ? result.missed++ : list.push(value));

  while (sent < options.keystrokes) {
    if (!root.isConnected) throw new Error("grille retirée de la page pendant la mesure");
    // Une ligne : Tab jusqu'à la dernière colonne (une ligne sur deux, saisie de la quantité), Entrée vers la
    // ligne suivante, puis quelques flèches ; toutes les 5 lignes, Page suivante.
    for (let c = 1; c < editableCount && sent < options.keystrokes; c++) {
      await sleep(cadence);
      if (c - 1 === options.quantityColumn && cycle % 2 === 0) {      // curseur sur la quantité
        const r = await typeAndCommit(String(1 + (cycle % 9)));
        record(result.entry, r.tab);
        record(result.amount, r.amount);
      } else {
        record(result.navigation, await navigate("Tab"));
      }
      sent++;
    }
    for (const key of ["Enter", "ArrowDown", "ArrowDown", "ArrowUp"]) {
      if (sent >= options.keystrokes) break;
      await sleep(cadence);
      record(result.navigation, await navigate(key));
      sent++;
    }
    if (cycle % 5 === 4 && sent < options.keystrokes) {
      await sleep(cadence);
      record(result.navigation, await navigate("PageDown"));
      sent++;
    }
    cycle++;
  }

  result.rows = Number(document.activeElement?.dataset?.r ?? 0);
  return result;
}

export function environment() {
  return {
    userAgent: navigator.userAgent,
    cores: navigator.hardwareConcurrency ?? 0,
    width: window.innerWidth,
    height: window.innerHeight,
    pixelRatio: window.devicePixelRatio,
  };
}

// Presse-papiers ; si la vue web le refuse, copie depuis la zone de texte du résultat.
export async function copyText(text) {
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    const area = document.getElementById("bench-text");
    if (!area) return false;
    area.focus();
    area.select();
    try {
      return document.execCommand("copy");
    } catch {
      return false;
    }
  }
}
