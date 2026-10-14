// Module clavier d'une grille de saisie Lettra (annexe A §4.2), issu du POC grille-clavier.
//
// La navigation pure (Tab, Entrée, flèches, Page, Échap, F2) est traitée ici, sans aucun appel .NET.
// .NET n'est appelé que pour :
//   - les commandes métier déclarées par le registre (F4, Insert, Ctrl+Delete…) ;
//   - la validation d'une cellule modifiée, à la sortie de la cellule (OnCellCommitted, une fois par saisie) ;
//   - la fin de grille (Entrée ou Tab sur la dernière cellule crée une ligne).
//
// Les cellules sont des <input class="lg-input" data-r="ligne" data-c="colonne">. La grille est
// virtualisée : une cellule hors écran n'existe pas dans le DOM, focusCell défile jusqu'à elle.

export function chordOf(e) {
  let key = e.key;
  if (key === " ") key = "Space";
  else if (key.length === 1) key = key.toUpperCase();
  return (e.ctrlKey || e.metaKey ? "Ctrl+" : "") + (e.altKey ? "Alt+" : "") + (e.shiftKey ? "Shift+" : "") + key;
}

export function attachGrid(root, dotnet, options) {
  const commands = new Set(options.commandKeys);
  const editable = options.editableColumns;          // index des colonnes saisissables, dans l'ordre
  const viewport = root.querySelector(".lg-viewport");
  let original = "";                                 // valeur de la cellule à son entrée (Échap y revient)
  let pending = Promise.resolve();                   // file des appels .NET, dans l'ordre des frappes
  let active = { r: 0, c: editable[0] };

  const rowCount = () => Number(root.getAttribute("aria-rowcount")) - 1;
  const cellAt = (r, c) => root.querySelector(`input.lg-input[data-r="${r}"][data-c="${c}"]`);
  const isCell = el => el instanceof HTMLInputElement && el.classList.contains("lg-input") && root.contains(el);

  // ---------- focus et défilement ----------

  function headerHeight() {
    return root.querySelector(".lg-header")?.offsetHeight ?? 0;
  }

  function ensureVisible(el) {
    const row = el.closest(".lg-row") ?? el;
    const v = viewport.getBoundingClientRect();
    const b = row.getBoundingClientRect();
    const top = v.top + headerHeight();
    if (b.top < top) viewport.scrollTop -= top - b.top;
    else if (b.bottom > v.bottom) viewport.scrollTop += b.bottom - v.bottom;
  }

  function rowHeight() {
    return root.querySelector(".lg-row[data-r]")?.offsetHeight || 24;
  }

  function renderedRange() {
    const rows = root.querySelectorAll(".lg-row[data-r]");
    if (rows.length === 0) return null;
    return { min: Number(rows[0].dataset.r), max: Number(rows[rows.length - 1].dataset.r) };
  }

  const nextFrame = () => new Promise(resolve => requestAnimationFrame(() => resolve()));

  function focusNow(el) {
    el.focus({ preventScroll: true });
    ensureVisible(el);
  }

  // Focalise une cellule ; si la ligne n'est pas rendue (virtualisation), défile puis attend son rendu.
  async function focusCell(r, c) {
    if (rowCount() <= 0) return false;
    r = Math.max(0, Math.min(r, rowCount() - 1));
    let el = cellAt(r, c);
    if (el) { focusNow(el); return true; }
    const h = rowHeight();
    viewport.scrollTop = Math.max(0, r * h - viewport.clientHeight / 2);
    for (let frame = 0; frame < 120; frame++) {
      await nextFrame();
      el = cellAt(r, c);
      if (el) { focusNow(el); return true; }
      const range = renderedRange();
      if (range && r < range.min) viewport.scrollTop -= (range.min - r) * h;
      else if (range && r > range.max) viewport.scrollTop += (r - range.max) * h;
    }
    return false;
  }

  // ---------- validation de cellule (seul appel .NET lié à la saisie) ----------

  // .NET renvoie le texte normalisé (« 1 234,50 ») ou l'ancien texte si la saisie est refusée :
  // le module le remet lui-même, car Blazor ne met pas à jour un <input> dont la valeur rendue n'a pas changé.
  function commit(r, c, text) {
    pending = pending.then(async () => {
      const result = await dotnet.invokeMethodAsync("OnCellCommitted", r, c, text);
      const el = cellAt(r, c);
      if (el && el.value === text) {
        el.value = result.text;
        if (el === document.activeElement) { original = result.text; el.select(); }
      }
      if (el && !result.ok) {
        el.classList.add("lg-error");
        setTimeout(() => el.classList.remove("lg-error"), 1500);
      }
    }).catch(err => console.error("Lettra grille : validation de cellule", err));
    return pending;
  }

  function commitIfDirty(el) {
    if (!isCell(el) || el.readOnly || el.value === original) return pending;
    const text = el.value;
    original = text;
    return commit(Number(el.dataset.r), Number(el.dataset.c), text);
  }

  // ---------- navigation ----------

  function step(r, c, dir) {
    let i = editable.indexOf(c) + dir;
    if (i >= editable.length) { i = 0; r++; }
    else if (i < 0) { i = editable.length - 1; r--; }
    return { r, c: editable[i] };
  }

  async function moveLogical(cell, r, c, dir) {
    const target = step(r, c, dir);
    if (target.r < 0) return;
    if (target.r >= rowCount()) {                      // fin de grille : la ligne suivante est créée par .NET
      await commitIfDirty(cell);
      await pending;
      const focus = await dotnet.invokeMethodAsync("OnEndReached", c);
      if (focus) await focusCell(focus.row, focus.col);
      return;
    }
    const sync = cellAt(target.r, target.c);
    if (sync) focusNow(sync); else await focusCell(target.r, target.c);
  }

  async function moveTo(r, c) {
    const sync = cellAt(r, c);
    if (sync) focusNow(sync); else await focusCell(r, c);
  }

  function pageSize() {
    return Math.max(1, Math.floor((viewport.clientHeight - headerHeight()) / rowHeight()) - 1);
  }

  async function runCommand(cell, chord, r, c) {
    commitIfDirty(cell);
    await pending;
    const focus = await dotnet.invokeMethodAsync("OnCommand", chord, r, c);
    if (focus) await focusCell(focus.row, focus.col);
  }

  function onKeyDown(e) {
    const cell = e.target;
    if (!isCell(cell) || e.isComposing) return;
    const r = Number(cell.dataset.r), c = Number(cell.dataset.c);
    const chord = chordOf(e);

    if (commands.has(chord)) {
      e.preventDefault();
      e.stopPropagation();
      runCommand(cell, chord, r, c).catch(err => console.error("Lettra grille : commande", err));
      return;
    }
    if (e.altKey) return;

    const editing = cell.dataset.edit === "1";
    switch (e.key) {
      case "Tab":
      case "Enter":
        if (e.ctrlKey) return;
        e.preventDefault();
        moveLogical(cell, r, c, e.shiftKey ? -1 : 1).catch(err => console.error("Lettra grille : navigation", err));
        break;
      case "ArrowUp":
      case "ArrowDown":
        e.preventDefault();
        moveTo(r + (e.key === "ArrowUp" ? -1 : 1), c);
        break;
      case "ArrowLeft":
      case "ArrowRight": {
        if (editing) return;                           // mode édition (F2) : les flèches bougent le curseur
        const i = editable.indexOf(c) + (e.key === "ArrowLeft" ? -1 : 1);
        e.preventDefault();
        if (i >= 0 && i < editable.length) moveTo(r, editable[i]);
        break;
      }
      case "PageUp":
      case "PageDown":
        e.preventDefault();
        moveTo(Math.max(0, Math.min(rowCount() - 1, r + (e.key === "PageUp" ? -1 : 1) * pageSize())), c);
        break;
      case "Home":
      case "End":
        if (!e.ctrlKey) return;                        // Début/Fin seuls : curseur dans la cellule
        e.preventDefault();
        moveTo(e.key === "Home" ? 0 : rowCount() - 1, c);
        break;
      case "F2":                                       // mode édition, comme un tableur : curseur en fin de texte
        e.preventDefault();
        cell.dataset.edit = "1";
        cell.setSelectionRange(cell.value.length, cell.value.length);
        break;
      case "Escape":                                   // annule la saisie en cours dans la cellule
        if (cell.value === original && !editing) return; // rien à annuler : Échap reste aux raccourcis globaux
        e.preventDefault();
        cell.value = original;
        delete cell.dataset.edit;
        cell.select();
        break;
    }
  }

  function onFocusIn(e) {
    const cell = e.target;
    if (!isCell(cell)) return;
    active = { r: Number(cell.dataset.r), c: Number(cell.dataset.c) };
    original = cell.value;
    delete cell.dataset.edit;
    cell.select();
    root.querySelector(".lg-row.lg-active")?.classList.remove("lg-active");
    cell.closest(".lg-row")?.classList.add("lg-active");
  }

  function onFocusOut(e) {
    const cell = e.target;
    if (!isCell(cell)) return;
    delete cell.dataset.edit;
    commitIfDirty(cell);
  }

  // Un clic sur une cellule calculée renvoie le focus vers la cellule saisissable active de la ligne.
  function onMouseDown(e) {
    const row = e.target.closest?.(".lg-row[data-r]");
    if (!row || e.target.closest("input")) return;
    e.preventDefault();
    focusCell(Number(row.dataset.r), active.c);
  }

  root.addEventListener("keydown", onKeyDown);
  root.addEventListener("focusin", onFocusIn);
  root.addEventListener("focusout", onFocusOut);
  root.addEventListener("mousedown", onMouseDown);

  return {
    focusCell: (r, c) => focusCell(r, c),
    activeCell: () => {
      const el = document.activeElement;
      return isCell(el) ? { row: Number(el.dataset.r), col: Number(el.dataset.c) } : { row: active.r, col: active.c };
    },
    flush: () => { commitIfDirty(document.activeElement); return pending; },
    dispose: () => {
      root.removeEventListener("keydown", onKeyDown);
      root.removeEventListener("focusin", onFocusIn);
      root.removeEventListener("focusout", onFocusOut);
      root.removeEventListener("mousedown", onMouseDown);
    },
  };
}

// Raccourcis globaux (portée « global » ou « page » du registre) : écoutés sur le document, grille comprise.
// Dans WebView2 (MAUI), désactiver AreBrowserAcceleratorKeysEnabled pour que F3, F5 ou Ctrl+F arrivent ici.
export function attachGlobal(dotnet, keys) {
  const set = new Set(keys);
  const onKeyDown = e => {
    if (e.defaultPrevented) return;
    const chord = chordOf(e);
    if (!set.has(chord)) return;
    e.preventDefault();
    dotnet.invokeMethodAsync("OnShortcut", chord);
  };
  document.addEventListener("keydown", onKeyDown);
  return { dispose: () => document.removeEventListener("keydown", onKeyDown) };
}
