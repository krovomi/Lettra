// Aide à la saisie (vague 5, lane F) : lecture du fichier des codes postaux, clavier des listes de suggestions,
// aperçu de la saisie rapide dans la grille. La logique (analyse, recherche) reste en C# ; ce module ne fait que le DOM.

export async function fetchText(url) {
  const response = await fetch(url, { cache: "force-cache" });
  return response.ok ? await response.text() : null;
}

// Liste de suggestions ouverte : Entrée choisit (sans envoyer le formulaire), les flèches parcourent la liste (sans
// déplacer le curseur), Échap la ferme (sans fermer le panneau autour). Blazor reçoit toujours la touche.
export function guardCombobox(input) {
  if (!input || input.dataset.ltGuard) { return; }
  input.dataset.ltGuard = "1";
  input.addEventListener("keydown", e => {
    if (input.getAttribute("aria-expanded") === "true" && ["Enter", "ArrowUp", "ArrowDown", "Escape"].includes(e.key)) {
      e.preventDefault();
      if (e.key === "Escape") { e.stopPropagation(); }
    }
  });
}

export function focusEnd(element) {
  if (!element) { return; }
  element.focus();
  const end = element.value?.length ?? 0;
  try { element.setSelectionRange(end, end); } catch { /* champ sans sélection */ }
}

// Aperçu de la saisie rapide (« 3h » → « 3 heures », « 2x45 » → « = 90,00 ») sous la cellule en cours, dans les
// colonnes données de la grille. Le texte vient de .NET (QuickEntry) ; rien n'est validé avant Entrée.
let previewHost = null;
export function attachQuickPreview(hostSelector, columns, dotnet) {
  detachQuickPreview();
  const host = document.querySelector(hostSelector);
  if (!host) { return; }
  const bubble = document.createElement("div");
  bubble.className = "lt-quick-preview";
  bubble.setAttribute("role", "status");
  bubble.hidden = true;
  document.body.appendChild(bubble);
  let token = 0;
  const hide = () => { bubble.hidden = true; };
  const onInput = async e => {
    const el = e.target;
    if (!(el instanceof HTMLInputElement) || !el.classList.contains("lg-input") || !columns.includes(Number(el.dataset.c))) { return; }
    const mine = ++token;
    const text = await dotnet.invokeMethodAsync("Preview", Number(el.dataset.c), el.value);
    if (mine !== token) { return; }
    if (!text) { hide(); return; }
    const r = el.getBoundingClientRect();
    bubble.textContent = text;
    bubble.style.top = `${r.bottom + 4}px`;
    bubble.style.left = `${Math.max(8, Math.min(r.left, innerWidth - 220))}px`;
    bubble.hidden = false;
  };
  host.addEventListener("input", onInput);
  host.addEventListener("focusout", hide);
  previewHost = { host, onInput, hide, bubble };
}

export function detachQuickPreview() {
  if (previewHost) {
    previewHost.host.removeEventListener("input", previewHost.onInput);
    previewHost.host.removeEventListener("focusout", previewHost.hide);
    previewHost.bubble.remove();
    previewHost = null;
  }
}
