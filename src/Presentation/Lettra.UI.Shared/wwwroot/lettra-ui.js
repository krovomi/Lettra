// Services navigateur de l'UI partagée (WASM et MAUI Hybrid) : téléchargement de fichiers, préférences locales.

export function downloadFile(fileName, mimeType, bytes) {
  // bytes arrive en Uint8Array (interop .NET byte[]).
  const blob = new Blob([bytes], { type: mimeType });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  link.rel = "noopener";
  document.body.appendChild(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
}

// Envoi d'un fichier choisi dans un champ, par le navigateur lui-même : il part du disque en flux, sans être copié
// dans la mémoire de WebAssembly (un paquet de dossier peut faire 512 Mio). Statut 0 : aucun fichier dans le champ.
export async function postFile(url, inputId, fileField, fields, headers) {
  const file = document.getElementById(inputId)?.files?.[0];
  if (!file) return { status: 0, contentType: null, body: null };
  const form = new FormData();
  form.append(fileField, file, file.name);
  for (const [name, value] of Object.entries(fields)) form.append(name, value);
  const response = await fetch(url, { method: "POST", body: form, headers, credentials: "same-origin" });
  return { status: response.status, contentType: response.headers.get("content-type"), body: await response.text() };
}

export function readPreference(key) {
  try { return localStorage.getItem(key); } catch { return null; }
}

export function writePreference(key, value) {
  try { localStorage.setItem(key, value); } catch { /* stockage indisponible : la préférence vaut pour la session */ }
}

// Densité par défaut selon la plateforme : Confort au doigt (tablette, mobile), Expert à la souris et au clavier.
export function prefersComfort() {
  return window.matchMedia ? window.matchMedia("(pointer: coarse)").matches : false;
}

// Thème (vague 5) : data-theme sur <html> ; null suit le système (prefers-color-scheme). La barre d'adresse mobile et
// la barre de titre de la PWA prennent la couleur du fond.
export function applyTheme(theme) {
  const root = document.documentElement;
  if (theme) { root.setAttribute("data-theme", theme); } else { root.removeAttribute("data-theme"); }
  const meta = document.querySelector('meta[name="theme-color"]');
  if (meta) {
    const color = getComputedStyle(root).getPropertyValue("--nav-bg").trim();
    if (color) { meta.setAttribute("content", color); }
  }
}
