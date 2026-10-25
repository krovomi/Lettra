// Démo commentée : les gestes que Lettra joue à la place de l'utilisateur (trouver un élément, le montrer, taper un
// texte à vitesse humaine, cliquer, choisir dans une liste). Rien n'est simulé côté serveur : ce sont les mêmes
// événements qu'un clavier et une souris, que les écrans Blazor reçoivent comme d'habitude.

let ring = null;
let pointer = null;

function visible(element) {
  if (!element || !element.isConnected) { return false; }
  const rect = element.getBoundingClientRect();
  if (rect.width === 0 && rect.height === 0) { return false; }
  const style = getComputedStyle(element);
  return style.visibility !== "hidden" && style.display !== "none";
}

// Sélecteur CSS, ou « css >> texte » : le premier élément visible du sélecteur qui contient ce texte.
function query(selector) {
  const [css, text] = selector.split(" >> ");
  let candidates = [];
  try { candidates = [...document.querySelectorAll(css)]; } catch { return null; }
  const wanted = text?.trim().toLocaleLowerCase();
  return candidates.find(e => visible(e) && (!wanted || e.textContent.toLocaleLowerCase().includes(wanted))) ?? null;
}

function sleep(ms) {
  return new Promise(resolve => setTimeout(resolve, ms));
}

function reducedMotion() {
  return matchMedia("(prefers-reduced-motion: reduce)").matches;
}

async function waitFor(selector, timeoutMs) {
  const until = performance.now() + timeoutMs;
  let element = query(selector);
  while (!element && performance.now() < until) {
    await sleep(100);
    element = query(selector);
  }
  return element;
}

function place(node, rect, pad) {
  node.style.top = `${rect.top - pad}px`;
  node.style.left = `${rect.left - pad}px`;
  node.style.width = `${rect.width + pad * 2}px`;
  node.style.height = `${rect.height + pad * 2}px`;
}

let follow = null;

function overlaps(a, b) {
  return a.left < b.right && b.left < a.right && a.top < b.bottom && b.top < a.bottom;
}

// La bulle ne doit pas cacher l'élément montré : sur ordinateur et tablette, elle monte en haut de l'écran (ou
// redescend), toujours à gauche, loin des messages de confirmation en bas à droite ; sur téléphone (feuille du bas),
// l'écran défile pour remonter l'élément au-dessus d'elle.
function keepVisible(element) {
  const card = document.getElementById("demo-card");
  if (!card) { return; }
  const target = element.getBoundingClientRect();
  const box = card.getBoundingClientRect();
  if (!overlaps(target, box)) { return; }
  if (innerWidth >= 768) {
    card.dataset.side = target.top + target.height / 2 > innerHeight / 2 ? "top" : "bottom";
  } else {
    window.scrollBy({ top: target.bottom - box.top + 16, behavior: reducedMotion() ? "auto" : "smooth" });
  }
}

function showRing(element) {
  if (!ring) {
    ring = document.createElement("div");
    ring.className = "lt-demo-ring";
    ring.setAttribute("aria-hidden", "true");
    document.body.appendChild(ring);
  }
  cancelAnimationFrame(follow);
  const track = () => {
    if (!element.isConnected) { hideRing(); return; }
    place(ring, element.getBoundingClientRect(), 4);
    follow = requestAnimationFrame(track);
  };
  ring.hidden = false;
  track();
  keepVisible(element);
}

function hideRing() {
  cancelAnimationFrame(follow);
  if (ring) { ring.hidden = true; }
}

// Pointeur animé jusqu'à l'élément : on voit où Lettra « clique ».
async function movePointer(element, speed) {
  if (!pointer) {
    pointer = document.createElement("div");
    pointer.className = "lt-demo-pointer";
    pointer.setAttribute("aria-hidden", "true");
    document.body.appendChild(pointer);
  }
  const rect = element.getBoundingClientRect();
  pointer.hidden = false;
  pointer.style.transitionDuration = reducedMotion() ? "0ms" : `${Math.round(450 / speed)}ms`;
  pointer.style.transform = `translate(${rect.left + Math.min(rect.width / 2, 40)}px, ${rect.top + rect.height / 2}px)`;
  if (!reducedMotion()) { await sleep(500 / speed); }
}

// Élément présent mais caché dans un bloc replié (bouton aria-expanded="false" qui contrôle un de ses parents) : le
// bloc est déplié, comme le ferait l'utilisateur.
function expand(selector) {
  const [css] = selector.split(" >> ");
  let element = null;
  try { element = document.querySelector(css); } catch { element = null; }
  for (let node = element; node && node !== document.body; node = node.parentElement) {
    if (!node.id) { continue; }
    const toggle = document.querySelector(`[aria-expanded="false"][aria-controls~="${CSS.escape(node.id)}"]`);
    if (toggle) { toggle.click(); return true; }
  }
  return false;
}

async function bring(selector, timeoutMs) {
  if (!query(selector) && expand(selector)) { await sleep(200); }
  const element = await waitFor(selector, timeoutMs);
  if (!element) { return null; }
  element.scrollIntoView({ block: "center", inline: "nearest", behavior: reducedMotion() ? "auto" : "smooth" });
  if (!reducedMotion()) { await sleep(300); }
  return element;
}

/** Montre l'élément (anneau autour) ; vrai s'il a été trouvé. Sans sélecteur, retire l'anneau. */
export async function highlight(selector, timeoutMs) {
  if (pointer) { pointer.hidden = true; }
  if (!selector) { hideRing(); return true; }
  const element = await bring(selector, timeoutMs);
  if (!element) { hideRing(); return false; }
  showRing(element);
  return true;
}

/** Attend qu'un élément apparaisse (écran qui charge, pièce qui s'enregistre). */
export async function waitForElement(selector, timeoutMs) {
  return (await waitFor(selector, timeoutMs)) !== null;
}

function setValue(element, value) {
  const prototype = element instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
  Object.getOwnPropertyDescriptor(prototype, "value").set.call(element, value);
  element.dispatchEvent(new Event("input", { bubbles: true }));
}

/** Tape le texte caractère par caractère, puis valide le champ (événement change, comme en quittant le champ). */
export async function type(selector, text, speed, timeoutMs) {
  const element = await bring(selector, timeoutMs);
  if (!element) { return false; }
  showRing(element);
  await movePointer(element, speed);
  element.focus();
  if (element instanceof HTMLSelectElement) {
    const option = [...element.options].find(o => o.value === text || o.text.trim() === text);
    if (!option) { return false; }
    element.value = option.value;
    element.dispatchEvent(new Event("change", { bubbles: true }));
    return true;
  }
  setValue(element, "");
  const delay = reducedMotion() ? 0 : 55 / speed;
  for (let i = 1; i <= text.length; i++) {
    setValue(element, text.slice(0, i));
    if (delay > 0) { await sleep(delay); }
  }
  element.dispatchEvent(new Event("change", { bubbles: true }));
  // Quitter le champ : la grille des lignes valide sa cellule à la sortie, comme avec Tab.
  element.blur();
  return true;
}

/** Clique l'élément après y avoir amené le pointeur. */
export async function click(selector, speed, timeoutMs) {
  const element = await bring(selector, timeoutMs);
  if (!element) { return false; }
  showRing(element);
  await movePointer(element, speed);
  element.click();
  return true;
}

/** Appuie sur une touche dans l'élément (Entrée pour valider une cellule, Échap…). */
export async function press(selector, key, timeoutMs) {
  const element = await waitFor(selector, timeoutMs);
  if (!element) { return false; }
  element.focus();
  for (const kind of ["keydown", "keyup"]) {
    element.dispatchEvent(new KeyboardEvent(kind, { key, code: key, bubbles: true, cancelable: true }));
  }
  return true;
}

/** Fin de la démo : anneau et pointeur retirés. */
export function clear() {
  hideRing();
  if (pointer) { pointer.hidden = true; }
}
