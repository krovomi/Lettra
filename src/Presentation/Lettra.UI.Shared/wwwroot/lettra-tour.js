// Tutoriel des premières fois (vague 5, lane F) : trouve l'élément montré, le fait défiler à l'écran, suit le
// défilement et le redimensionnement, et rend le focus là où il était à la fin du parcours.

let watcher = null;
let keys = null;
let lastFocus = null;

// Démarrage automatique seulement dans un vrai navigateur ou WebView : jamais sous un navigateur piloté (tests
// Playwright, captures), où la bulle couvrirait l'écran testé. Le bouton « ? » reste disponible partout.
export function canAutoStart() {
  return !navigator.webdriver;
}

function visible(element) {
  if (!element || !element.isConnected) { return false; }
  const rect = element.getBoundingClientRect();
  if (rect.width === 0 && rect.height === 0) { return false; }
  const style = getComputedStyle(element);
  return style.visibility !== "hidden" && style.display !== "none";
}

function find(selectors) {
  for (const selector of selectors) {
    let element = null;
    try { element = document.querySelector(selector); } catch { element = null; }
    if (visible(element)) { return element; }
  }
  return null;
}

// Un élément présent mais caché dans un bloc replié (bouton aria-expanded="false" qui contrôle un de ses parents) :
// on déplie le bloc pour pouvoir le montrer. Rend vrai si un bloc a été déplié.
function expand(selectors) {
  for (const selector of selectors) {
    let element = null;
    try { element = document.querySelector(selector); } catch { element = null; }
    for (let node = element; node && node !== document.body; node = node.parentElement) {
      if (!node.id) { continue; }
      const toggle = document.querySelector(`[aria-expanded="false"][aria-controls~="${CSS.escape(node.id)}"]`);
      if (toggle) { toggle.click(); return true; }
    }
  }
  return false;
}

function rectOf(element) {
  const r = element.getBoundingClientRect();
  return { top: r.top, left: r.left, width: r.width, height: r.height, viewportWidth: innerWidth, viewportHeight: innerHeight };
}

function viewport() {
  return { top: 0, left: 0, width: 0, height: 0, viewportWidth: innerWidth, viewportHeight: innerHeight };
}

// Attend l'élément (l'écran peut encore charger ses données), le fait défiler au milieu, puis rend sa position.
// Sans élément après le délai : position vide, la bulle se met au centre.
export async function locate(selectors, timeoutMs) {
  if (!selectors || selectors.length === 0) { return viewport(); }
  const until = performance.now() + (timeoutMs ?? 1500);
  let element = find(selectors);
  if (!element && expand(selectors)) {
    await new Promise(resolve => setTimeout(resolve, 150));
    element = find(selectors);
  }
  while (!element && performance.now() < until) {
    await new Promise(resolve => setTimeout(resolve, 100));
    element = find(selectors);
  }
  if (!element) { return viewport(); }
  const reduce = matchMedia("(prefers-reduced-motion: reduce)").matches;
  element.scrollIntoView({ block: "center", inline: "nearest", behavior: reduce ? "auto" : "smooth" });
  if (!reduce) { await new Promise(resolve => setTimeout(resolve, 250)); }
  return rectOf(element);
}

// Suit l'élément pendant que la bulle est ouverte (défilement, rotation du téléphone, clavier virtuel).
export function watch(dotnet, selectors) {
  unwatch();
  let pending = false;
  const update = () => {
    if (pending) { return; }
    pending = true;
    requestAnimationFrame(() => {
      pending = false;
      const element = find(selectors);
      dotnet.invokeMethodAsync("OnViewportChanged", element ? rectOf(element) : viewport());
    });
  };
  addEventListener("resize", update, { passive: true });
  addEventListener("scroll", update, { passive: true, capture: true });
  watcher = update;

  // Échap ferme la bulle même si le focus n'y est pas encore (l'écran charge encore) ; les flèches aussi, sauf
  // dans un champ de saisie. Dans la bulle, c'est le composant qui traite la touche.
  keys = event => {
    if (event.defaultPrevented || event.target?.closest?.(".lt-tour")) { return; }
    const editable = event.target?.closest?.("input, textarea, select, [contenteditable='true']");
    if (event.key === "Escape" || (!editable && (event.key === "ArrowRight" || event.key === "ArrowLeft"))) {
      event.preventDefault();
      dotnet.invokeMethodAsync("OnKey", event.key);
    }
  };
  document.addEventListener("keydown", keys);
}

export function unwatch() {
  if (watcher) {
    removeEventListener("resize", watcher);
    removeEventListener("scroll", watcher, { capture: true });
    watcher = null;
  }
  if (keys) {
    document.removeEventListener("keydown", keys);
    keys = null;
  }
}

export function rememberFocus() {
  lastFocus = document.activeElement;
}

export function restoreFocus() {
  if (lastFocus && lastFocus.isConnected && typeof lastFocus.focus === "function") { lastFocus.focus(); }
  lastFocus = null;
}
